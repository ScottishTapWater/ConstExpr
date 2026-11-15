using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using ConstExpr.Core.Evaluation;
using ConstExpr.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ConstExpr.Roslyn.Generators;

/// <summary>
/// Provides the ConstExpr incremental source generator entry point.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConstExprGenerator : IIncrementalGenerator
{
#pragma warning disable RS2008 // This initial diagnostic is shipped with the first implementation.
	private static readonly DiagnosticDescriptor _evaluationFailed = new(
		"CONSTEXPR001", "Compile-time evaluation failed", "{0}", "ConstExpr", DiagnosticSeverity.Error, true);
#pragma warning restore RS2008

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<InvocationExpressionSyntax> calls = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is InvocationExpressionSyntax,
			static (syntax, _) => (InvocationExpressionSyntax)syntax.Node);
		context.RegisterSourceOutput(context.CompilationProvider.Combine(calls.Collect()),
			static (output, input) => Generate(output, (CSharpCompilation)input.Left, input.Right));
	}

	private static void Generate(SourceProductionContext context, CSharpCompilation compilation, ImmutableArray<InvocationExpressionSyntax> calls)
	{
		Assembly? assembly = null;
		Dictionary<IMethodSymbol, string> results = new(SymbolEqualityComparer.Default);
		MethodEvaluator evaluator = new();
		ResultSerializer serializer = new();
		StringBuilder interceptors = new();
		int index = 0;
		foreach (InvocationExpressionSyntax call in calls)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			SemanticModel model = compilation.GetSemanticModel(call.SyntaxTree);
			if (model.GetSymbolInfo(call, context.CancellationToken).Symbol is not IMethodSymbol method
				|| !IsSupported(method) || model.GetInterceptorMethod(call) is not null)
			{
				continue;
			}
			InterceptableLocation? location = model.GetInterceptableLocation(call, context.CancellationToken);
			if (location is null)
			{
				continue;
			}
			try
			{
				if (!results.TryGetValue(method, out string? literal))
				{
					assembly ??= CompileForEvaluation(compilation, context.CancellationToken);
					Type type = assembly.GetType(GetMetadataName(method.ContainingType), throwOnError: true)!;
					MethodInfo entrypoint = type.GetMethod(method.MetadataName,
						BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!;
					literal = serializer.Serialize(evaluator.Evaluate(entrypoint), typeof(int));
					results.Add(method, literal);
				}
				interceptors.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")]");
				interceptors.AppendLine($"public static int Invoke{index++}() => {literal};");
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				context.ReportDiagnostic(Diagnostic.Create(_evaluationFailed, call.GetLocation(), exception.GetBaseException().Message));
				return;
			}
		}
		if (interceptors.Length > 0)
		{
			context.AddSource("ConstExpr.Interceptors.g.cs", $$"""
				namespace System.Runtime.CompilerServices
				{
					[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
					file sealed class InterceptsLocationAttribute : System.Attribute
					{
						public InterceptsLocationAttribute(int version, string data) { }
					}
				}
				namespace ConstExpr.Generated
				{
					file static class Interceptors
					{
						{{interceptors}}
					}
				}
				""");
		}
	}

	private static bool IsSupported(IMethodSymbol method)
	{
		return method.IsStatic && method.MethodKind == MethodKind.Ordinary
			&& method.Parameters.Length == 0 && !method.IsGenericMethod && !method.ContainingType.IsGenericType
			&& !method.ReturnsByRef && !method.ReturnsByRefReadonly && method.ReturnType.SpecialType == SpecialType.System_Int32
			&& method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "ConstExpr.ConstExprAttribute");
	}

	private static string GetMetadataName(INamedTypeSymbol type)
	{
		return type.ContainingType is not null
			? GetMetadataName(type.ContainingType) + "+" + type.MetadataName
			: type.ToDisplayString();
	}

	private static Assembly CompileForEvaluation(CSharpCompilation compilation, CancellationToken cancellationToken)
	{
		using MemoryStream stream = new();
		EmitResult result = compilation.WithAssemblyName(compilation.AssemblyName + ".ConstExprEvaluation")
			.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n",
				result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
#pragma warning disable RS1035 // Executing the user's method is the purpose of this generator.
		return Assembly.Load(stream.ToArray());
#pragma warning restore RS1035
	}
}
