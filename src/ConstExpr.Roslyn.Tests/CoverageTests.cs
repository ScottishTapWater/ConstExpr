using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class CoverageTests
{
	[Test]
	public async Task EveryCallSiteInTheSameMethodUsesAPrecomputedResult()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				[ConstExpr]
				public static int CountUp() => ++counter;

				public static ExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = CountUp() + CountUp();
					}
					return new ExecutionResult(values, Calls);
				}
			}
			""";
		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Fixture.Counter.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { result.Values[0], result.Values[0], result.Values[0] });
		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	[Test]
	public async Task CallSitesInDifferentSourceFilesUsePrecomputedResults()
	{
		const string first = "namespace Fixture; public static class First { public static int Next() => Counter.CountUp(); }";
		const string second = "namespace Fixture; public static class Second { public static int Next() => Counter.CountUp(); }";
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[
			new("Counter.cs", FixtureSources.Counter), new("First.cs", first), new("Second.cs", second),
			new("Caller.cs", FixtureSources.Caller("First.Next() + Second.Next()"))
			], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { result.Values[0], result.Values[0], result.Values[0] });
		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToDifferentMarkedMethodsUseTheirOwnPrecomputedResults()
	{
		const string other = """
			using ConstExpr;

			namespace Fixture;

			public static class OtherCounter
			{
				private static int counter;
				public static int Calls => counter;

				[ConstExpr]
				public static int CountUp() => ++counter;
			}
			""";
		const string caller = """
			using ConstExpr.Roslyn.Tests;

			namespace Fixture;

			public static class Caller
			{
				public static CoverageTests.TwoCounterExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Counter.CountUp() + OtherCounter.CountUp();
					}
					return new CoverageTests.TwoCounterExecutionResult(values, Counter.Calls, OtherCounter.Calls);
				}
			}
			""";
		TwoCounterExecutionResult result = await ConstExprFixture.CompileAndRun<TwoCounterExecutionResult>(
			[
			new("Counter.cs", FixtureSources.Counter), new("OtherCounter.cs", other),
			new("Caller.cs", caller)
			], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 2, 2, 2 });
		await Assert.That(result.CounterRuntimeCalls).IsEqualTo(0);
		await Assert.That(result.OtherCounterRuntimeCalls).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToUnmarkedMethodsExecuteTheOriginalMethodAtRuntime()
	{
		const string source = """
			using ConstExpr.Roslyn.Tests.Infrastructure;

			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				public static int CountUp() => ++counter;

				public static ExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = CountUp();
					}
					return new ExecutionResult(values, Calls);
				}
			}
			""";
		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Fixture.Counter.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(result.RuntimeCalls).IsEqualTo(3);
	}

	public sealed record TwoCounterExecutionResult(int[] Values, int CounterRuntimeCalls, int OtherCounterRuntimeCalls);
}
