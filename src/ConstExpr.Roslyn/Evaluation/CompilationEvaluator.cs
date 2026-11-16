using System.IO;
using System.Reflection;
using ConstExpr.Core.Evaluation;
using ConstExpr.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CompilationEvaluator
{
	private readonly CSharpCompilation _compilation;
	private readonly MethodEvaluator _evaluator;
	private readonly ResultSerializer _serializer;
	private readonly Dictionary<IMethodSymbol, Dictionary<int, string>> _results = new(SymbolEqualityComparer.Default);
	private Assembly? _assembly;

	internal CompilationEvaluator(CSharpCompilation compilation, MethodEvaluator evaluator, ResultSerializer serializer)
	{
		_compilation = compilation;
		_evaluator = evaluator;
		_serializer = serializer;
	}

	internal string Evaluate(IMethodSymbol method, int? argument, CancellationToken cancellationToken)
	{
		if (!_results.TryGetValue(method, out Dictionary<int, string>? methodResults))
		{
			methodResults = new();
			_results.Add(method, methodResults);
		}
		if (!methodResults.TryGetValue(argument.GetValueOrDefault(), out string? literal))
		{
			_assembly ??= Compile(cancellationToken);
			Type type = _assembly.GetType(GetMetadataName(method.ContainingType), throwOnError: true)!;
			Type[] parameterTypes = argument.HasValue ? [typeof(int)] : Type.EmptyTypes;
			MethodInfo entrypoint = type.GetMethod(method.MetadataName,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null)!;
			object?[] arguments = argument.HasValue ? [argument.Value] : [];
			literal = _serializer.Serialize(_evaluator.Evaluate(entrypoint, arguments), typeof(int));
			methodResults.Add(argument.GetValueOrDefault(), literal);
		}
		return literal;
	}

	private Assembly Compile(CancellationToken cancellationToken)
	{
		using MemoryStream stream = new();
		EmitResult result = _compilation.WithAssemblyName(_compilation.AssemblyName + ".ConstExprEvaluation")
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

	private static string GetMetadataName(INamedTypeSymbol type)
	{
		return type.ContainingType is not null
			? GetMetadataName(type.ContainingType) + "+" + type.MetadataName
			: type.ToDisplayString();
	}
}
