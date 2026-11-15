using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

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
		string marker = name + "Attribute";
		StatementSyntax[] helpers = CreateHelpers(call, model, returnType, name, marker, cancellationToken);
		SyntaxNode root = call.SyntaxTree.GetRoot(cancellationToken);
		SyntaxNode? scope = call.Ancestors().FirstOrDefault(node => node is BlockSyntax or ArrowExpressionClauseSyntax or LambdaExpressionSyntax);
		if (scope is LambdaExpressionSyntax lambda && model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol converted
			&& SymbolEqualityComparer.Default.Equals(converted.OriginalDefinition, model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1")))
		{
			scope = lambda.Ancestors().FirstOrDefault(node => node is BlockSyntax or ArrowExpressionClauseSyntax);
		}
		SyntaxNode updated = scope switch
		{
			BlockSyntax block => root.ReplaceNode(block, AppendHelpers(block, helpers, block.CloseBraceToken.GetLocation())),
			ArrowExpressionClauseSyntax arrow => ReplaceArrowBody(root, arrow, helpers, model, cancellationToken),
			_ => root.ReplaceNode(call, InlineHelpers(call, helpers, returnType, name))
		};
		SyntaxTree tree = call.SyntaxTree.WithRootAndOptions(updated, call.SyntaxTree.Options);
		// nameof marks reflection-only local functions as used without suppressing compiler warnings.
		SyntaxTree attribute = CSharpSyntaxTree.ParseText($"internal sealed class {marker} : global::System.Attribute {{ public {marker}(string name) {{ }} }}", (CSharpParseOptions)call.SyntaxTree.Options);
		CSharpCompilation compilation = ((CSharpCompilation)model.Compilation).ReplaceSyntaxTree(call.SyntaxTree, tree)
			.AddSyntaxTrees(attribute).WithAssemblyName(model.Compilation.AssemblyName + "." + name);
		using MemoryStream stream = new();
		EmitResult result = compilation.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
		Assembly assembly = _loader.Load(stream.ToArray());
		MethodInfo entrypoint = assembly.GetTypes()
			.SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
			.Single(method => method.GetCustomAttributesData().Any(attributeData => attributeData.AttributeType.FullName == marker));
		if (entrypoint.ContainsGenericParameters || entrypoint.GetParameters().Length != 0)
		{
			throw new InvalidOperationException("The expression requires a closed, parameterless evaluation function.");
		}
		return entrypoint;
	}

	private static StatementSyntax[] CreateHelpers(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType,
		string name, string marker, CancellationToken cancellationToken)
	{
		SyntaxNode? overflow = call.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
		bool isChecked = overflow is null ? ((CSharpCompilationOptions)model.Compilation.Options).CheckOverflow
			: overflow.IsKind(SyntaxKind.CheckedExpression) || overflow.IsKind(SyntaxKind.CheckedStatement);
		string expression = $"{(isChecked ? "checked" : "unchecked")}(\n{LineDirective(call.GetLocation())}{call}\n)";
		string type = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		bool awaits = call.DescendantNodes().OfType<AwaitExpressionSyntax>().Any(awaitExpression => SymbolEqualityComparer.Default.Equals(
			model.GetEnclosingSymbol(awaitExpression.SpanStart, cancellationToken), model.GetEnclosingSymbol(call.SpanStart, cancellationToken)));
		if (awaits)
		{
			return
			[
				SyntaxFactory.ParseStatement($"static async global::System.Threading.Tasks.Task<{type}> {name}Awaited() => {expression};\n"),
				SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}))] static {type} {name}() => {name}Awaited().GetAwaiter().GetResult();\n")
			];
		}
		return [SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}))] static {type} {name}() => {expression};\n")];
	}

	private static BlockSyntax AppendHelpers(BlockSyntax block, StatementSyntax[] helpers, Location end)
	{
		SyntaxTriviaList restore = SyntaxFactory.ParseLeadingTrivia("\n" + LineDirective(end));
		return block.AddStatements(helpers).WithCloseBraceToken(block.CloseBraceToken.WithLeadingTrivia(block.CloseBraceToken.LeadingTrivia.AddRange(restore)));
	}

	private static string LineDirective(Location location)
	{
		FileLinePositionSpan mapped = location.GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		return $"#line {mapped.StartLinePosition.Line + 1} {path}\n";
	}

	private static ExpressionSyntax InlineHelpers(InvocationExpressionSyntax call, StatementSyntax[] helpers, ITypeSymbol returnType, string name)
	{
		// Initialisers and expression-bodied lambdas need a block without leaving their lexical scope.
		string type = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		string declarations = string.Join("\n", helpers.Select(helper => helper.ToFullString()));
		ExpressionSyntax expression = SyntaxFactory.ParseExpression($"((global::System.Func<{type}>)(static () => {{\n{declarations}\nreturn {name}();\n}}))()");
		FileLinePositionSpan mapped = call.GetLastToken().GetLocation().GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		SyntaxTriviaList restore = SyntaxFactory.ParseLeadingTrivia($"\n#line {mapped.EndLinePosition.Line + 1} {path}\n");
		return expression.WithLeadingTrivia(call.GetLeadingTrivia()).WithTrailingTrivia(restore.AddRange(call.GetTrailingTrivia()));
	}

	private static SyntaxNode ReplaceArrowBody(SyntaxNode root, ArrowExpressionClauseSyntax arrow, StatementSyntax[] helpers,
		SemanticModel model, CancellationToken cancellationToken)
	{
		SyntaxNode member = arrow.Parent!;
		bool returnsVoid = model.GetDeclaredSymbol(member, cancellationToken) is IMethodSymbol { ReturnsVoid: true };
		StatementSyntax original = arrow.Expression is ThrowExpressionSyntax thrown ? SyntaxFactory.ThrowStatement(thrown.Expression)
			: returnsVoid ? SyntaxFactory.ExpressionStatement(arrow.Expression) : SyntaxFactory.ReturnStatement(arrow.Expression);
		SyntaxToken semicolon = member.GetLastToken();
		BlockSyntax body = AppendHelpers(SyntaxFactory.Block(original).WithCloseBraceToken(
			SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(semicolon.TrailingTrivia)), helpers, semicolon.GetLocation());
		SyntaxNode replacement = member switch
		{
			MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			LocalFunctionStatementSyntax local => local.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			ConstructorDeclarationSyntax constructor => constructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			DestructorDeclarationSyntax destructor => destructor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			OperatorDeclarationSyntax operation => operation.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			ConversionOperatorDeclarationSyntax conversion => conversion.WithExpressionBody(null).WithSemicolonToken(default).WithBody(body),
			PropertyDeclarationSyntax property => property.WithExpressionBody(null).WithSemicolonToken(default)
				.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
			IndexerDeclarationSyntax indexer => indexer.WithExpressionBody(null).WithSemicolonToken(default)
				.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(body)))),
			_ => throw new InvalidOperationException("The expression has no executable body for evaluation.")
		};
		return root.ReplaceNode(member, replacement);
	}
}
