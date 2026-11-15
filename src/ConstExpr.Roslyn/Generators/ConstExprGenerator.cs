using ConstExpr.Core.Evaluation;
using ConstExpr.Core.Serialization;
using ConstExpr.Roslyn.Evaluation;
using ConstExpr.Roslyn.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Generators;

/// <summary>
/// Provides the ConstExpr incremental source generator entry point.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConstExprGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<InvocationExpressionSyntax> calls = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is InvocationExpressionSyntax,
			static (syntax, _) => (InvocationExpressionSyntax)syntax.Node);
		context.RegisterSourceOutput(context.CompilationProvider.Combine(calls.Collect()),
			static (output, input) =>
			{
				CSharpCompilation compilation = (CSharpCompilation)input.Left;
				CompilationEvaluator evaluator = new(compilation, new MethodEvaluator(), new ResultSerializer());
				CallSiteProcessor processor = new(evaluator, new InterceptorSourceBuilder());
				processor.Process(output, compilation, input.Right);
			});
	}
}
