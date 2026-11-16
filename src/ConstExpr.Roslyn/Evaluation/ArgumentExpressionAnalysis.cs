using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class ArgumentExpressionAnalysis : CSharpSyntaxWalker
{
	private readonly SyntaxNode _call;
	private readonly SemanticModel _model;
	private readonly CancellationToken _cancellationToken;
	private bool _canEvaluate = true;

	private ArgumentExpressionAnalysis(SyntaxNode call, SemanticModel model, CancellationToken cancellationToken)
	{
		_call = call;
		_model = model;
		_cancellationToken = cancellationToken;
	}

	internal static bool CanEvaluate(IInvocationOperation invocation, SemanticModel model, CancellationToken cancellationToken)
	{
		ArgumentExpressionAnalysis analysis = new(invocation.Syntax, model, cancellationToken);
		foreach (IArgumentOperation argument in invocation.Arguments)
		{
			analysis.Visit(argument.Value.Syntax);
		}
		return analysis._canEvaluate;
	}

	internal static bool HasClosedTypes(IMethodSymbol method) => !ContainsTypeParameter(method.ContainingType)
		&& !method.TypeArguments.Any(ContainsTypeParameter);

	private static bool ContainsTypeParameter(ITypeSymbol? type) => type switch
	{
		ITypeParameterSymbol => true,
		IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
		IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
		IFunctionPointerTypeSymbol pointer => ContainsTypeParameter(pointer.Signature.ReturnType)
			|| pointer.Signature.Parameters.Any(parameter => ContainsTypeParameter(parameter.Type)),
		INamedTypeSymbol named => ContainsTypeParameter(named.ContainingType) || named.TypeArguments.Any(ContainsTypeParameter),
		_ => false
	};

	public override void Visit(SyntaxNode? node)
	{
		_cancellationToken.ThrowIfCancellationRequested();
		if (node is null || !_canEvaluate)
		{
			return;
		}
		if (node is ExpressionSyntax expression)
		{
			TypeInfo type = _model.GetTypeInfo(expression, _cancellationToken);
			if (ContainsFreeType(type.Type) || ContainsFreeType(type.ConvertedType))
			{
				_canEvaluate = false;
				return;
			}
			// A fixed value can still have a caller-dependent type, such as a boxed Generic<T>.Enum.
			if (_model.GetConstantValue(expression, _cancellationToken).HasValue)
			{
				return;
			}
		}
		IOperation? operation = _model.GetOperation(node, _cancellationToken);
		if (operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
			|| operation is IMemberReferenceOperation { Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } }
			|| operation is IDynamicMemberReferenceOperation { Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } }
			|| operation is IInvocationOperation { Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } })
		{
			_canEvaluate = false;
			return;
		}
		if (node is SimpleNameSyntax name && name.Parent is not NameColonSyntax)
		{
			ISymbol? symbol = _model.GetSymbolInfo(name, _cancellationToken).Symbol;
			_canEvaluate = symbol switch
			{
				ITypeParameterSymbol parameter => IsDeclaredInsideCall(parameter),
				IParameterSymbol parameter => IsDeclaredInsideCall(parameter),
				ILocalSymbol local => local.IsConst || IsDeclaredInsideCall(local),
				IRangeVariableSymbol variable => IsDeclaredInsideCall(variable),
				IMethodSymbol { MethodKind: MethodKind.LocalFunction } method => IsDeclaredInsideCall(method),
				_ => true
			};
		}
		if (_canEvaluate)
		{
			base.Visit(node);
		}
	}

	private bool ContainsFreeType(ITypeSymbol? type) => type switch
	{
		ITypeParameterSymbol parameter => !IsDeclaredInsideCall(parameter),
		IArrayTypeSymbol array => ContainsFreeType(array.ElementType),
		IPointerTypeSymbol pointer => ContainsFreeType(pointer.PointedAtType),
		IFunctionPointerTypeSymbol pointer => ContainsFreeType(pointer.Signature.ReturnType)
			|| pointer.Signature.Parameters.Any(parameter => ContainsFreeType(parameter.Type)),
		INamedTypeSymbol named => ContainsFreeType(named.ContainingType) || named.TypeArguments.Any(ContainsFreeType),
		_ => false
	};

	private bool IsDeclaredInsideCall(ISymbol symbol) => symbol.DeclaringSyntaxReferences
		.Any(reference => reference.SyntaxTree == _call.SyntaxTree && _call.Span.Contains(reference.Span));
}
