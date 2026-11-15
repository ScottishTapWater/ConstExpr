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
		string contextTypes = GenericContextTypes.Find(call, model, cancellationToken);
		StatementSyntax[] helpers = CreateHelpers(call, model, returnType, name, marker, contextTypes, cancellationToken);
		SyntaxNode root = call.SyntaxTree.GetRoot(cancellationToken);
		LambdaExpressionSyntax[] quotedScopes = call.Ancestors().OfType<LambdaExpressionSyntax>()
			.Where(lambda => model.GetTypeInfo(lambda, cancellationToken).ConvertedType is INamedTypeSymbol converted
				&& SymbolEqualityComparer.Default.Equals(converted.OriginalDefinition, model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1")))
			.Reverse().ToArray();
		ExpressionSyntax extracted = InlineHelpers(call, helpers, returnType, name);
		SyntaxNode updated = quotedScopes.Length == 0 ? root.ReplaceNode(call, extracted)
			: root.ReplaceNode(quotedScopes[0], PreserveQuotedScope(quotedScopes, 0, call, extracted, model, marker, cancellationToken));
		SyntaxTree tree = call.SyntaxTree.WithRootAndOptions(updated, call.SyntaxTree.Options);
		// nameof marks reflection-only local functions as used without suppressing compiler warnings.
		SyntaxTree attribute = CSharpSyntaxTree.ParseText($"internal sealed class {marker} : global::System.Attribute {{ public {marker}(string name, params global::System.Type[] contextTypes) {{ }} public static T PreserveQuote<T>(T expression, global::System.Delegate scope) => expression; }}", (CSharpParseOptions)call.SyntaxTree.Options);
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
		entrypoint = EvaluationGenericContext.Close(entrypoint, marker, cancellationToken);
		if (entrypoint.ContainsGenericParameters || entrypoint.GetParameters().Length != 0)
		{
			throw new InvalidOperationException("The expression requires a closed, parameterless evaluation function.");
		}
		return entrypoint;
	}

	private static StatementSyntax[] CreateHelpers(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType,
		string name, string marker, string contextTypes, CancellationToken cancellationToken)
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
				SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}){contextTypes})] static {type} {name}() => {name}Awaited().GetAwaiter().GetResult();\n")
			];
		}
		return [SyntaxFactory.ParseStatement($"[global::{marker}(nameof({name}){contextTypes})] static {type} {name}() => {expression};\n")];
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
		return RestoreSourceEnd(SyntaxFactory.ParseExpression($"global::{marker}.PreserveQuote<{quotedType}>(\n{LineDirective(original.GetLocation())}{original},\n{LineDirective(original.GetLocation())}({delegateType})({executable}))"), original);
	}
}
