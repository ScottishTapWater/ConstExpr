using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class PipelineTests
{
	[Test]
	public async Task GeneratesAnInterceptorForADirectCall()
	{
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", FixtureSources.Simple));

		GeneratorRun run = GeneratorRun.Create(input);

		await Assert.That(run.HasInterceptor).IsTrue();
	}

	[Test]
	public async Task GeneratedInterceptorCompiles()
	{
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", FixtureSources.Simple));
		GeneratorRun run = GeneratorRun.Create(input);
		await Assert.That(run.HasInterceptor).IsTrue();

		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);

		await Assert.That(emitted.Success).IsTrue();
	}

	[Test]
	public async Task RepeatedDirectCallsReturnTheBakedValue()
	{
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", FixtureSources.Simple));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
	}

	[Test]
	public async Task InterceptedCallsDoNotExecuteTheOriginalMethod()
	{
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", FixtureSources.Simple));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);

		await fixture.Run();

		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task UnmarkedMethodProducesNoGeneratedSource()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Counter.cs", FixtureSources.Counter.Replace("[ConstExpr]", "", StringComparison.Ordinal)));
		test.TestState.Sources.Add(("Caller.cs", FixtureSources.Caller()));

		await test.RunAsync();
	}
}
