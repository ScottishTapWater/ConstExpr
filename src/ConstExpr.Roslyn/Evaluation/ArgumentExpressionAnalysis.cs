using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class ArgumentExpressionAnalysis : OperationWalker
{
	private readonly SyntaxNode _call;
	private bool _canEvaluate = true;

	private ArgumentExpressionAnalysis(SyntaxNode call)
	{
		_call = call;
	}

	internal static bool CanEvaluate(IInvocationOperation invocation)
	{
		ArgumentExpressionAnalysis analysis = new(invocation.Syntax);
		foreach (IArgumentOperation argument in invocation.Arguments)
		{
			analysis.Visit(argument.Value);
		}
		return analysis._canEvaluate;
	}

	public override void Visit(IOperation? operation)
	{
		if (operation is null || operation.ConstantValue.HasValue)
		{
			return;
		}
		if (operation.Type is { } type && ContainsTypeParameter(type))
		{
			_canEvaluate = false;
		}
		if (operation is IMemberReferenceOperation member && ContainsTypeParameter(member.Member.ContainingType))
		{
			_canEvaluate = false;
		}
		base.Visit(operation);
	}

	public override void VisitLocalReference(ILocalReferenceOperation operation)
	{
		_canEvaluate &= operation.Local.IsConst || IsDeclaredInsideCall(operation.Local);
	}

	public override void VisitParameterReference(IParameterReferenceOperation operation)
	{
		_canEvaluate &= IsDeclaredInsideCall(operation.Parameter);
	}

	public override void VisitInstanceReference(IInstanceReferenceOperation operation)
	{
		_canEvaluate &= operation.ReferenceKind != InstanceReferenceKind.ContainingTypeInstance;
	}

	public override void VisitInvocation(IInvocationOperation operation)
	{
		_canEvaluate &= !ContainsTypeParameter(operation.TargetMethod.ContainingType)
			&& !operation.TargetMethod.TypeArguments.Any(ContainsTypeParameter)
			&& (operation.TargetMethod.MethodKind != MethodKind.LocalFunction || IsDeclaredInsideCall(operation.TargetMethod));
		base.VisitInvocation(operation);
	}

	public override void VisitMethodReference(IMethodReferenceOperation operation)
	{
		_canEvaluate &= operation.Method.MethodKind != MethodKind.LocalFunction || IsDeclaredInsideCall(operation.Method);
		_canEvaluate &= !operation.Method.TypeArguments.Any(ContainsTypeParameter);
		base.VisitMethodReference(operation);
	}

	public override void VisitTypeOf(ITypeOfOperation operation)
	{
		_canEvaluate &= !ContainsTypeParameter(operation.TypeOperand);
	}

	private bool IsDeclaredInsideCall(ISymbol symbol) => symbol.DeclaringSyntaxReferences
		.Any(reference => reference.SyntaxTree == _call.SyntaxTree && _call.Span.Contains(reference.Span));

	private static bool ContainsTypeParameter(ITypeSymbol type) => type is ITypeParameterSymbol
		|| type is IArrayTypeSymbol array && ContainsTypeParameter(array.ElementType)
		|| type is INamedTypeSymbol named && (named.TypeArguments.Any(ContainsTypeParameter)
			|| named.ContainingType is not null && ContainsTypeParameter(named.ContainingType));
}
