using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests;

public sealed class PipelineTests
{
	[Test]
	public async Task EvaluationFailureReportsABuildErrorAtTheCallSite()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			public static class Counter
			{
				[ConstExpr]
				public static int CountUp() => throw new System.InvalidOperationException("Evaluation failed.");
				public static int Run() => {|#0:CountUp()|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Evaluation failed."));

		await test.RunAsync();
	}

	[Test]
	public async Task GeneratesAnInterceptorForACallToAMarkedMethod()
	{
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", FixtureSources.Simple));

		GeneratorRun run = GeneratorRun.Create(input);

		await Assert.That(run.HasInterceptor).IsTrue();
	}

	[Test]
	public async Task GeneratedInterceptorCanBeEmittedAsAnAssembly()
	{
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", FixtureSources.Simple));
		GeneratorRun run = GeneratorRun.Create(input);
		await Assert.That(run.HasInterceptor).IsTrue();

		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);

		await Assert.That(emitted.Success).IsTrue();
	}

	[Test]
	public async Task RepeatedCallsToAMarkedMethodReturnItsBuildTimeResult()
	{
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Program.cs", FixtureSources.Simple)], "Fixture.Counter.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 });
	}

	[Test]
	public async Task InterceptedCallsDoNotExecuteTheOriginalMethodAtRuntime()
	{
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Program.cs", FixtureSources.Simple)], "Fixture.Counter.Run");

		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToUnmarkedMethodsProduceNoGeneratedSource()
	{
		const string counter = """
			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				public static int CountUp() => ++counter;
			}
			""";
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Counter.cs", counter));
		test.TestState.Sources.Add(("Caller.cs", FixtureSources.Caller()));

		await test.RunAsync();
	}
}
