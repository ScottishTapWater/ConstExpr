using System.IO;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CallExpressionCompiler
{
	private readonly EvaluationAssemblyLoader _loader;

	internal CallExpressionCompiler(EvaluationAssemblyLoader loader)
	{
		_loader = loader;
	}

	internal MethodInfo Compile(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType, CancellationToken cancellationToken)
	{
		string name = "__ConstExprEvaluate" + Guid.NewGuid().ToString("N");
		ExpressionSyntax expression = (ExpressionSyntax)new CallExpressionRewriter(model).Visit(call)!;
		SyntaxNode? checkedContext = call.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
		if (checkedContext is not null)
		{
			SyntaxKind kind = checkedContext.IsKind(SyntaxKind.CheckedExpression) || checkedContext.IsKind(SyntaxKind.CheckedStatement)
				? SyntaxKind.CheckedExpression : SyntaxKind.UncheckedExpression;
			expression = SyntaxFactory.CheckedExpression(kind, expression);
		}
		MethodDeclarationSyntax entrypoint = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), name)
			.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
			.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(expression))
			.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

		SyntaxNode root = call.SyntaxTree.GetRoot(cancellationToken);
		TypeDeclarationSyntax? containingType = call.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
		SyntaxNode updated = containingType is not null
			? root.ReplaceNode(containingType, containingType.AddMembers(entrypoint))
			: ((CompilationUnitSyntax)root).AddMembers(SyntaxFactory.ClassDeclaration(name)
				.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
				.AddMembers(entrypoint));
		SyntaxTree tree = call.SyntaxTree.WithRootAndOptions(updated, call.SyntaxTree.Options);
		CSharpCompilation compilation = ((CSharpCompilation)model.Compilation).ReplaceSyntaxTree(call.SyntaxTree, tree)
			.WithAssemblyName(model.Compilation.AssemblyName + "." + name);
		using MemoryStream stream = new();
		EmitResult result = compilation.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
		Assembly assembly = _loader.Load(stream.ToArray());
		return assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			.Single(method => method.Name == name);
	}

	private sealed class CallExpressionRewriter : CSharpSyntaxRewriter
	{
		private readonly SemanticModel _model;

		internal CallExpressionRewriter(SemanticModel model)
		{
			_model = model;
		}

		public override SyntaxNode? Visit(SyntaxNode? node)
		{
			if (node is IdentifierNameSyntax identifier && _model.GetSymbolInfo(identifier).Symbol is ILocalSymbol { IsConst: true } local)
			{
				VariableDeclaratorSyntax declaration = (VariableDeclaratorSyntax)local.DeclaringSyntaxReferences.Single().GetSyntax();
				ExpressionSyntax initializer = (ExpressionSyntax)Visit(declaration.Initializer!.Value)!;
				return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
					SyntaxFactory.ParenthesizedExpression(initializer));
			}
			if (node is InvocationExpressionSyntax && _model.GetOperation(node) is INameOfOperation nameOf)
			{
				return ConstantExpression(nameOf.ConstantValue.Value, nameOf.Type!);
			}
			return base.Visit(node);
		}

		public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
		{
			InvocationExpressionSyntax rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
			return _model.GetOperation(node) is IInvocationOperation invocation
				? rewritten.WithArgumentList(PreserveCallerInformation(rewritten.ArgumentList, invocation.Arguments)) : rewritten;
		}

		public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			ObjectCreationExpressionSyntax rewritten = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
			return _model.GetOperation(node) is IObjectCreationOperation creation
				? rewritten.WithArgumentList(PreserveCallerInformation(rewritten.ArgumentList ?? SyntaxFactory.ArgumentList(), creation.Arguments)) : rewritten;
		}

		public override SyntaxNode? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
		{
			ImplicitObjectCreationExpressionSyntax rewritten = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node)!;
			return _model.GetOperation(node) is IObjectCreationOperation creation
				? rewritten.WithArgumentList(PreserveCallerInformation(rewritten.ArgumentList, creation.Arguments)) : rewritten;
		}

		private static ArgumentListSyntax PreserveCallerInformation(ArgumentListSyntax arguments, ImmutableArray<IArgumentOperation> boundArguments)
		{
			foreach (IArgumentOperation argument in boundArguments.Where(argument => argument.IsImplicit
				&& argument.ArgumentKind == ArgumentKind.DefaultValue && HasCallerInformation(argument.Parameter!)))
			{
				arguments = arguments.AddArguments(SyntaxFactory.Argument(ConstantExpression(argument.Value.ConstantValue.Value, argument.Parameter!.Type))
					.WithNameColon(SyntaxFactory.NameColon((IdentifierNameSyntax)SyntaxFactory.ParseName("@" + argument.Parameter.Name))));
			}
			return arguments;
		}

		private static bool HasCallerInformation(IParameterSymbol parameter) => parameter.GetAttributes().Any(attribute =>
			attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
			&& attribute.AttributeClass.Name is "CallerMemberNameAttribute" or "CallerFilePathAttribute" or "CallerLineNumberAttribute" or "CallerArgumentExpressionAttribute");

		private static ExpressionSyntax ConstantExpression(object? value, ITypeSymbol type)
		{
			string literal = value is null ? "null" : SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)!;
			return SyntaxFactory.ParseExpression($"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({literal})");
		}
	}
}
