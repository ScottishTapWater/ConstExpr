using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class ConstExprFixture
{
	internal static Task<ExecutionResult> CompileAndRun(SourceFile[] sources, string entrypoint) =>
		CompileAndRun<ExecutionResult>(sources, entrypoint);

	internal static async Task<TResult> CompileAndRun<TResult>(SourceFile[] sources, string entrypoint)
	{
		CSharpCompilation input = await CompilationFixture.Create(sources);
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);
		return await fixture.Run<TResult>(entrypoint);
	}
}
