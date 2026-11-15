using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class ConstExprFixture
{
	internal static Task<ExecutionResult> CompileAndRun(SourceFile[] sources, string entrypoint, CSharpCompilationOptions? options = null) =>
		CompileAndRun<ExecutionResult>(sources, entrypoint, options);

	internal static async Task<TResult> CompileAndRun<TResult>(SourceFile[] sources, string entrypoint, CSharpCompilationOptions? options = null)
	{
		CSharpCompilation input = options is null
			? await CompilationFixture.Create(sources)
			: await CompilationFixture.CreateWithOptions(sources, options);
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);
		return await fixture.Run<TResult>(entrypoint);
	}
}
