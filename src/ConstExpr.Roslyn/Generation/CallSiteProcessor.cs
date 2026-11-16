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
			if (!TryGetConstantArguments(invocation, out object?[] arguments))
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
				EvaluatedResult result = _evaluator.Evaluate(method, arguments, context.CancellationToken);
				_source.Add(location, method, result);
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
			&& method.Parameters.All(parameter => IsConstantType(parameter.Type) && parameter.RefKind == RefKind.None)
			&& !method.ReturnsVoid && !method.ReturnsByRef && !method.ReturnsByRefReadonly
			&& !method.ReturnType.IsRefLikeType && method.ReturnType.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer)
			&& method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "ConstExpr.ConstExprAttribute");
	}

	private static bool IsConstantType(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
		SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_String
		or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
		or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
		or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

	private static bool TryGetConstantArguments(IInvocationOperation invocation, out object?[] arguments)
	{
		arguments = new object?[invocation.TargetMethod.Parameters.Length];
		foreach (IArgumentOperation argument in invocation.Arguments)
		{
			if (argument.Value.ConstantValue is not { HasValue: true } constant)
			{
				return false;
			}
			arguments[argument.Parameter!.Ordinal] = constant.Value;
		}
		return true;
	}
}
