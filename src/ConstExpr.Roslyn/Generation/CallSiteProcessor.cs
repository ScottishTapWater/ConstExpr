using System.Collections.Immutable;
using ConstExpr.Roslyn.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Generation;

internal sealed class CallSiteProcessor
{
#pragma warning disable RS2008 // Diagnostic release tracking is not configured.
	private static readonly DiagnosticDescriptor _evaluationFailed = new(
		"CONSTEXPR001", "Compile-time evaluation failed", "{0}", "ConstExpr", DiagnosticSeverity.Error, true);
	private static readonly DiagnosticDescriptor _nonconstantArgument = new(
		"CONSTEXPR002", "Argument is not a compile-time constant",
		"Call to '{0}' will run at runtime because at least one argument is not a compile-time constant",
		"ConstExpr", DiagnosticSeverity.Warning, true);
#pragma warning restore RS2008

	private readonly CompilationEvaluator _evaluator;
	private readonly InterceptorSourceBuilder _source;

	internal CallSiteProcessor(CompilationEvaluator evaluator, InterceptorSourceBuilder source)
	{
		_evaluator = evaluator;
		_source = source;
	}

	internal void Process(SourceProductionContext context, CSharpCompilation compilation, ImmutableArray<InvocationExpressionSyntax> calls)
	{
		foreach (InvocationExpressionSyntax call in calls)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			SemanticModel model = compilation.GetSemanticModel(call.SyntaxTree);
			if (model.GetOperation(call, context.CancellationToken) is not IInvocationOperation invocation
				|| !IsSupported(invocation.TargetMethod) || model.GetInterceptorMethod(call) is not null)
			{
				continue;
			}
			IMethodSymbol method = invocation.TargetMethod;
			if (!TryGetConstantArguments(invocation, out int[] arguments))
			{
				context.ReportDiagnostic(Diagnostic.Create(_nonconstantArgument, call.GetLocation(), method.ToDisplayString()));
				continue;
			}
			InterceptableLocation? location = model.GetInterceptableLocation(call, context.CancellationToken);
			if (location is null)
			{
				continue;
			}
			try
			{
				string literal = _evaluator.Evaluate(method, arguments, context.CancellationToken);
				_source.Add(location, method.Parameters, literal);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				context.ReportDiagnostic(Diagnostic.Create(_evaluationFailed, call.GetLocation(), exception.GetBaseException().Message));
				return;
			}
		}
		if (_source.HasInterceptors)
		{
			context.AddSource("ConstExpr.Interceptors.g.cs", _source.Build());
		}
	}

	private static bool IsSupported(IMethodSymbol method)
	{
		return method.IsStatic && method.MethodKind == MethodKind.Ordinary
			&& !method.IsGenericMethod && !method.ContainingType.IsGenericType
			&& method.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Int32 && parameter.RefKind == RefKind.None)
			&& !method.ReturnsByRef && !method.ReturnsByRefReadonly && method.ReturnType.SpecialType == SpecialType.System_Int32
			&& method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "ConstExpr.ConstExprAttribute");
	}

	private static bool TryGetConstantArguments(IInvocationOperation invocation, out int[] arguments)
	{
		arguments = new int[invocation.TargetMethod.Parameters.Length];
		foreach (IArgumentOperation argument in invocation.Arguments)
		{
			if (argument.Value.ConstantValue is not { HasValue: true, Value: int value })
			{
				return false;
			}
			arguments[argument.Parameter!.Ordinal] = value;
		}
		return true;
	}
}
