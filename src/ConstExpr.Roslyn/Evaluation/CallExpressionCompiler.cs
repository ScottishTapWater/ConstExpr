using System.IO;
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
		string marker = name + "Attribute";
		SyntaxAnnotation annotation = new(name);
		bool generic = GenericEvaluationScope.IsRequired(call, model, cancellationToken);
		InvocationExpressionSyntax expression = generic ? (InvocationExpressionSyntax)new ExpressionSourceNormalizer(model, call, cancellationToken).Visit(call)! : call;
		expression = expression.WithAdditionalAnnotations(annotation);
		StatementSyntax[] helpers = CreateHelpers(call, expression, model, returnType, name, marker, cancellationToken);
		SyntaxNode root = call.SyntaxTree.GetRoot(cancellationToken);
		SyntaxNode updated;
		if (generic)
		{
			helpers[0] = helpers[0].WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia("\n" + EvaluationSourceContext.Capture(model, call.SpanStart)).AddRange(helpers[0].GetLeadingTrivia()));
			updated = GenericEvaluationScope.Create(root, call, helpers, model, name, cancellationToken);
		}
		else
		{
			LambdaExpressionSyntax[] quotedScopes = call.Ancestors().OfType<LambdaExpressionSyntax>()
				.Where(lambda => model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol converted
					&& SymbolEqualityComparer.Default.Equals(converted.OriginalDefinition, model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1")))
				.Reverse().ToArray();
			ExpressionSyntax extracted = InlineHelpers(call, helpers, returnType, name);
			updated = quotedScopes.Length == 0 ? root.ReplaceNode(call, extracted)
				: root.ReplaceNode(quotedScopes[0], PreserveQuotedScope(quotedScopes, 0, call, extracted, model, marker, cancellationToken));
		}
		SyntaxTree tree = call.SyntaxTree.WithRootAndOptions(updated, call.SyntaxTree.Options);
		// nameof marks reflection-only local functions as used without suppressing compiler warnings.
		SyntaxTree attribute = CSharpSyntaxTree.ParseText($"internal sealed class {marker} : global::System.Attribute {{ public {marker}(string name) {{ }} public static T PreserveQuote<T>(T expression, global::System.Delegate scope) => expression; }}", (CSharpParseOptions)call.SyntaxTree.Options);
		CSharpCompilation compilation = ((CSharpCompilation)model.Compilation).ReplaceSyntaxTree(call.SyntaxTree, tree)
			.AddSyntaxTrees(attribute);
		SyntaxNode extractedCall = tree.GetRoot(cancellationToken).GetAnnotatedNodes(annotation).Single();
		IOperation originalBinding = model.GetOperation(call, cancellationToken)!;
		IOperation extractedBinding = compilation.GetSemanticModel(tree).GetOperation(extractedCall, cancellationToken)
			?? throw new InvalidOperationException("The extracted expression has no compiler binding.");
		ExpressionBindingComparer.Verify(originalBinding, extractedBinding, cancellationToken);
		compilation = compilation.WithAssemblyName(model.Compilation.AssemblyName + "." + name);
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

	private static StatementSyntax[] CreateHelpers(InvocationExpressionSyntax call, InvocationExpressionSyntax expressionCall,
		SemanticModel model, ITypeSymbol returnType, string name, string marker, CancellationToken cancellationToken)
	{
		SyntaxNode? overflow = call.Ancestors().FirstOrDefault(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);
		bool isChecked = overflow is null ? ((CSharpCompilationOptions)model.Compilation.Options).CheckOverflow
			: overflow.IsKind(SyntaxKind.CheckedExpression) || overflow.IsKind(SyntaxKind.CheckedStatement);
		string expression = $"{(isChecked ? "checked" : "unchecked")}(\n{LineDirective(call.GetLocation())}__ConstExprInput\n)";
		string type = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
		bool awaits = call.DescendantNodes().OfType<AwaitExpressionSyntax>().Any(awaitExpression => SymbolEqualityComparer.Default.Equals(
			model.GetEnclosingSymbol(awaitExpression.SpanStart, cancellationToken), model.GetEnclosingSymbol(call.SpanStart, cancellationToken)));
		bool unsafeContext = call.Ancestors().Any(node => node.ChildTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword)));
		string unsafeModifier = unsafeContext ? "unsafe " : "";
		StatementSyntax[] helpers = awaits
			? [SyntaxFactory.ParseStatement($"static async global::System.Threading.Tasks.Task<{type}> {name}Awaited() => {expression};\n"),
				SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}))] static {type} {name}() => {name}Awaited().GetAwaiter().GetResult();\n")]
			: [SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}))] static {unsafeModifier}{type} {name}() => {expression};\n")];
		for (int index = 0; index < helpers.Length; index++)
		{
			IdentifierNameSyntax? placeholder = helpers[index].DescendantNodes().OfType<IdentifierNameSyntax>().SingleOrDefault(node => node.Identifier.Text == "__ConstExprInput");
			if (placeholder is not null)
			{
				helpers[index] = helpers[index].ReplaceNode(placeholder, expressionCall.WithLeadingTrivia(placeholder.GetLeadingTrivia()));
			}
		}
		return helpers;
	}

	internal static string LineDirective(Location location)
	{
		FileLinePositionSpan mapped = location.GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		return $"#line {mapped.StartLinePosition.Line + 1} {path}\n";
	}

	private static ExpressionSyntax InlineHelpers(InvocationExpressionSyntax call, StatementSyntax[] helpers, ITypeSymbol returnType, string name)
	{
		// Initialisers and expression-bodied lambdas need a block without leaving their lexical scope.
		string type = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
		ExpressionSyntax expression = SyntaxFactory.ParseExpression($"((global::System.Func<{type}>)(static () => {{ __ConstExprBlock; }}))()");
		BlockSyntax body = expression.DescendantNodes().OfType<BlockSyntax>().Single();
		expression = expression.ReplaceNode(body, body.WithStatements(SyntaxFactory.List(helpers).Add(SyntaxFactory.ParseStatement($"return {name}();"))));
		return RestoreSourceEnd(expression.WithLeadingTrivia(call.GetLeadingTrivia()), call);
	}

	private static ExpressionSyntax RestoreSourceEnd(ExpressionSyntax expression, ExpressionSyntax original)
	{
		FileLinePositionSpan mapped = original.GetLastToken().GetLocation().GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		SyntaxTriviaList restore = SyntaxFactory.ParseLeadingTrivia($"\n#line {mapped.EndLinePosition.Line + 1} {path}\n");
		return expression.WithTrailingTrivia(restore.AddRange(original.GetTrailingTrivia()));
	}

	private static ExpressionSyntax PreserveQuotedScope(LambdaExpressionSyntax[] scopes, int index, InvocationExpressionSyntax call,
		ExpressionSyntax extracted, SemanticModel model, string marker, CancellationToken cancellationToken)
	{
		LambdaExpressionSyntax original = scopes[index];
		LambdaExpressionSyntax executable = index + 1 == scopes.Length ? original.ReplaceNode(call, extracted)
			: original.ReplaceNode(scopes[index + 1], PreserveQuotedScope(scopes, index + 1, call, extracted, model, marker, cancellationToken));
		INamedTypeSymbol expressionType = (INamedTypeSymbol)model.GetTypeInfo(original, cancellationToken).ConvertedType!;
		string quotedType = expressionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		string delegateType = expressionType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		// Keep the quoted lambda intact; a delegate copy supplies an executable lexical scope for the helper.
		ExpressionSyntax result = SyntaxFactory.ParseExpression($"global::{marker}.PreserveQuote<{quotedType}>(__ConstExprQuote, ({delegateType})(__ConstExprDelegate))");
		IdentifierNameSyntax quote = result.DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node.Identifier.Text == "__ConstExprQuote");
		IdentifierNameSyntax implementation = result.DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node.Identifier.Text == "__ConstExprDelegate");
		SyntaxTriviaList mapping = SyntaxFactory.ParseLeadingTrivia("\n" + LineDirective(original.GetLocation()));
		result = result.ReplaceNodes([quote, implementation], (node, _) => node == quote ? original.WithLeadingTrivia(mapping) : executable.WithLeadingTrivia(mapping));
		return RestoreSourceEnd(result, original);
	}
}
