using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ConstExpr.Roslyn.Generators;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal sealed class GeneratorRun
{
	private GeneratorRun(GeneratorDriver driver, Compilation compilation)
	{
		Driver = driver;
		Compilation = compilation;
	}

	internal GeneratorDriver Driver { get; }
	internal Compilation Compilation { get; }
	internal GeneratorDriverRunResult Result => Driver.GetRunResult();

	internal bool HasInterceptor => Compilation.SyntaxTrees.Except(Result.GeneratedTrees)
		.Any(tree =>
		{
			SemanticModel model = Compilation.GetSemanticModel(tree);
			return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
				.Any(invocation => model.GetInterceptorMethod(invocation) is not null);
		});

	internal static GeneratorRun Create(CSharpCompilation input, GeneratorDriver? driver = null)
	{
		driver ??= CSharpGeneratorDriver.Create([new ConstExprGenerator().AsSourceGenerator()], parseOptions: CompilationFixture.ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics);
		CompilationFixture.ThrowOnErrors(diagnostics);
		return new(driver, output);
	}
}
