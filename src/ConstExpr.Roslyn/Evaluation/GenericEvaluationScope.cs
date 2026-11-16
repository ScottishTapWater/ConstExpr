using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal static class GenericEvaluationScope
{
	internal static bool IsRequired(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
	{
		for (ISymbol? symbol = model.GetEnclosingSymbol(call.SpanStart, cancellationToken); symbol is not null; symbol = symbol.ContainingSymbol)
		{
			if (symbol is IMethodSymbol { IsGenericMethod: true })
			{
				return true;
			}
		}
		return false;
	}

	internal static SyntaxNode Create(SyntaxNode root, InvocationExpressionSyntax call, StatementSyntax[] helpers,
		SemanticModel model, string name, CancellationToken cancellationToken)
	{
		ISymbol? caller = model.GetEnclosingSymbol(call.SpanStart, cancellationToken);
		while (caller is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
		{
			caller = caller.ContainingSymbol;
		}
		string? boundCallerName = model.GetOperation(call, cancellationToken)!.DescendantsAndSelf().OfType<IArgumentOperation>()
			.Where(argument => argument.IsImplicit && argument.Parameter?.GetAttributes().Any(attribute =>
				attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.CallerMemberNameAttribute") == true)
			.Select(argument => argument.Value.ConstantValue.Value).OfType<string>().FirstOrDefault();
		string callerName = boundCallerName ?? (caller is IMethodSymbol { AssociatedSymbol: { } associated } ? associated.MetadataName : caller!.Name);
		string frameName = name + "Scope";
		string interfaceName = name + "Caller";
		BlockSyntax body = SyntaxFactory.Block(helpers);
		ClassDeclarationSyntax frame = SyntaxFactory.ClassDeclaration(frameName)
			.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.SealedKeyword)));
		MemberDeclarationSyntax[] members;
		if (caller is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } constructor)
		{
			ConstructorDeclarationSyntax member = SyntaxFactory.ConstructorDeclaration(frameName).WithBody(body);
			if (constructor.MethodKind == MethodKind.StaticConstructor)
			{
				member = member.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
			}
			members = [frame.AddMembers(member)];
		}
		else
		{
			SyntaxToken identifier = SyntaxFactory.Identifier(default, SyntaxKind.IdentifierToken, "@" + callerName, callerName, default);
			MethodDeclarationSyntax signature = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), identifier)
				.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
			InterfaceDeclarationSyntax contract = SyntaxFactory.InterfaceDeclaration(interfaceName).AddMembers(signature);
			MethodDeclarationSyntax member = signature.WithExplicitInterfaceSpecifier(SyntaxFactory.ExplicitInterfaceSpecifier(SyntaxFactory.IdentifierName(interfaceName)))
				.WithSemicolonToken(default).WithBody(body);
			frame = frame.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(interfaceName))))).AddMembers(member);
			members = [contract, frame];
		}
		TypeDeclarationSyntax? container = call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
		if (container is null)
		{
			return ((CompilationUnitSyntax)root).AddMembers(members);
		}
		TypeDeclarationSyntax changed = container.AddMembers(members);
		SyntaxTriviaList restore = SyntaxFactory.ParseLeadingTrivia("\n" + EvaluationSourceContext.Capture(model, container.CloseBraceToken.SpanStart) + EvaluationSourceContext.LineDirective(container.CloseBraceToken.GetLocation()));
		changed = changed.WithCloseBraceToken(changed.CloseBraceToken.WithLeadingTrivia(changed.CloseBraceToken.LeadingTrivia.AddRange(restore)));
		return root.ReplaceNode(container, changed);
	}
}
