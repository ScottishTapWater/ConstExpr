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
	private static readonly DiagnosticDescriptor _callerRuntimeState = new(
		"CONSTEXPR002", "Argument requires caller runtime state",
		"Call to '{0}' will run at runtime because at least one argument requires caller runtime state",
		"ConstExpr", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor _runtimeArgumentEvaluation = new(
		"CONSTEXPR004", "Argument expressions still execute at runtime",
		"Call to '{0}' uses a precomputed result; argument expressions still execute at runtime but their values are ignored. Move argument construction inside the [ConstExpr] method to avoid this runtime work.",
		"ConstExpr", DiagnosticSeverity.Warning, true);
	private static readonly DiagnosticDescriptor _inaccessibleSignature = new(
		"CONSTEXPR005", "Signature types are inaccessible to generated interceptors",
		"Call to '{0}' will run at runtime because its signature types are inaccessible to generated interceptors",
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
			if (!IsAccessible(method.ReturnType, compilation) || method.Parameters.Any(parameter => !IsAccessible(parameter.Type, compilation)))
			{
				context.ReportDiagnostic(Diagnostic.Create(_inaccessibleSignature, call.GetLocation(), method.ToDisplayString()));
				continue;
			}
			bool constantArguments = TryGetConstantArguments(invocation, out object?[] arguments);
			bool scalarArguments = constantArguments && method.Parameters.All(parameter => IsConstantType(parameter.Type));
			if (!ArgumentExpressionAnalysis.CanEvaluate(invocation, model, context.CancellationToken)
				|| !scalarArguments && call.Ancestors().OfType<TypeDeclarationSyntax>().Any(type => model.GetDeclaredSymbol(type)?.IsGenericType == true))
			{
				context.ReportDiagnostic(Diagnostic.Create(_callerRuntimeState, call.GetLocation(), method.ToDisplayString()));
				continue;
			}
			InterceptableLocation? location = model.GetInterceptableLocation(call, context.CancellationToken);
			if (location is null)
			{
				continue;
			}
			try
			{
				EvaluatedResult result = scalarArguments
					? _evaluator.Evaluate(method, arguments, context.CancellationToken)
					: _evaluator.EvaluateExpression(call, model, method.ReturnType, context.CancellationToken);
				_source.Add(location, method, result);
				if (!constantArguments)
				{
					context.ReportDiagnostic(Diagnostic.Create(_runtimeArgumentEvaluation, call.GetLocation(), method.ToDisplayString()));
				}
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
			&& method.Parameters.All(parameter => parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType
				&& parameter.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer))
			&& !method.ReturnsVoid && !method.ReturnsByRef && !method.ReturnsByRefReadonly
			&& !method.ReturnType.IsRefLikeType && method.ReturnType.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer)
			&& method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "ConstExpr.ConstExprAttribute");
	}

	private static bool IsConstantType(ITypeSymbol type) => type.TypeKind == TypeKind.Enum || type.SpecialType is
		SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_String
		or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
		or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
		or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

	private static bool IsAccessible(ITypeSymbol type, Compilation compilation) => type switch
	{
		IArrayTypeSymbol array => IsAccessible(array.ElementType, compilation),
		INamedTypeSymbol named => !named.IsFileLocal && compilation.IsSymbolAccessibleWithin(named, compilation.Assembly)
			&& named.TypeArguments.All(argument => IsAccessible(argument, compilation))
			&& (named.ContainingType is null || IsAccessible(named.ContainingType, compilation)),
		_ => true
	};

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
