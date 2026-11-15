using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class ExpressionSourceNormalizer : CSharpSyntaxRewriter
{
	private readonly SemanticModel _model;
	private readonly SyntaxNode _call;
	private readonly CancellationToken _cancellationToken;
	private readonly HashSet<string> _objectMembers;

	internal ExpressionSourceNormalizer(SemanticModel model, SyntaxNode call, CancellationToken cancellationToken)
	{
		_model = model;
		_call = call;
		_cancellationToken = cancellationToken;
		_objectMembers = new(model.Compilation.GetSpecialType(SpecialType.System_Object).GetMembers().Select(member => member.Name), StringComparer.Ordinal);
	}

	public override SyntaxNode? Visit(SyntaxNode? node)
	{
		_cancellationToken.ThrowIfCancellationRequested();
		if (node is null)
		{
			return null;
		}
		if (_model.GetOperation(node, _cancellationToken) is INameOfOperation nameOf)
		{
			return SourceTextPreserver.Preserve(Constant(nameOf.ConstantValue.Value, nameOf.Type!), node);
		}
		if (node is SimpleNameSyntax name)
		{
			ISymbol? symbol = _model.GetSymbolInfo(name, _cancellationToken).Symbol;
			if (symbol is ILocalSymbol { IsConst: true } local && !IsInsideCall(local))
			{
				VariableDeclaratorSyntax declaration = (VariableDeclaratorSyntax)local.DeclaringSyntaxReferences.Single().GetSyntax(_cancellationToken);
				TypeSyntax sourceType = ((VariableDeclarationSyntax)declaration.Parent!).Type;
				return SourceTextPreserver.Preserve(Constant(local.ConstantValue, local.Type, (TypeSyntax)Visit(sourceType)!), node);
			}
			if (!IsQualifiedName(name) && symbol is not null && _objectMembers.Contains(symbol.Name)
				&& (symbol.IsStatic || symbol is INamedTypeSymbol) && FindContainingScope(symbol) is { } scope)
			{
				string container = scope.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				SimpleNameSyntax rewritten = (SimpleNameSyntax)base.Visit(node)!;
				ExpressionSyntax qualified = symbol is INamedTypeSymbol
					? SyntaxFactory.QualifiedName(SyntaxFactory.ParseName(container), rewritten.WithoutTrivia())
					: SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ParseExpression(container), rewritten.WithoutTrivia());
				return SourceTextPreserver.Preserve(qualified, node);
			}
		}
		return base.Visit(node);
	}

	private INamedTypeSymbol? FindContainingScope(ISymbol symbol)
	{
		for (INamedTypeSymbol? scope = _model.GetEnclosingSymbol(_call.SpanStart, _cancellationToken)?.ContainingType; scope is not null; scope = scope.ContainingType)
		{
			for (INamedTypeSymbol? inherited = scope; inherited is not null; inherited = inherited.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(inherited, symbol.ContainingType))
				{
					return scope;
				}
			}
		}
		return null;
	}

	private bool IsInsideCall(ISymbol symbol) => symbol.DeclaringSyntaxReferences
		.Any(reference => reference.SyntaxTree == _call.SyntaxTree && _call.Span.Contains(reference.Span));

	private static bool IsQualifiedName(SimpleNameSyntax name) => name.Parent is MemberAccessExpressionSyntax member && member.Name == name
		|| name.Parent is MemberBindingExpressionSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax
		|| name.Parent is NameColonSyntax;

	private static ExpressionSyntax Constant(object? value, ITypeSymbol type, TypeSyntax? sourceType = null)
	{
		TypeSyntax constantType = sourceType?.WithoutTrivia() ?? SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
			.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)));
		// Default expressions keep null and Boolean values typed when token spelling is replaced.
		// Rewritten literal keyword tokens would instead acquire a string Value.
		ExpressionSyntax literal = value switch
		{
			null => SyntaxFactory.DefaultExpression(constantType),
			bool boolean => boolean
				? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.DefaultExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))))
				: SyntaxFactory.DefaultExpression(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))),
			string text => SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(text)),
			char character => SyntaxFactory.LiteralExpression(SyntaxKind.CharacterLiteralExpression, SyntaxFactory.Literal(character)),
			_ => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, value switch
			{
				sbyte or byte or short or ushort or int => SyntaxFactory.Literal(Convert.ToInt32(value)),
				uint number => SyntaxFactory.Literal(number),
				long number => SyntaxFactory.Literal(number),
				ulong number => SyntaxFactory.Literal(number),
				float number => SyntaxFactory.Literal(number),
				double number => SyntaxFactory.Literal(number),
				decimal number => SyntaxFactory.Literal(number),
				_ => throw new InvalidOperationException($"Unsupported constant type '{type}'.")
			})
		};
		return SyntaxFactory.CastExpression(constantType, literal);
	}
}
