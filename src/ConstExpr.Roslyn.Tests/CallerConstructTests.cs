using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class CallerConstructTests
{
	[Test]
	[MethodDataSource(nameof(SynchronousCallers))]
	public async Task CallsInsideCallerConstructsReturnPrecomputedResults(CallerConstructCase testCase)
	{
		string caller = FixtureSources.Caller(testCase.Expression, testCase.Declaration);
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", caller)], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	[Test]
	public async Task CallsAfterAwaitReturnPrecomputedResults()
	{
		const string caller = """
			using System.Threading.Tasks;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			namespace Fixture;

			public static class Caller
			{
				public static async Task<ExecutionResult> Run()
				{
					static async Task<int> Next()
					{
						await Task.Yield();
						return Counter.CountUp();
					}

					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = await Next();
					}
					return new ExecutionResult(values, Counter.Calls);
				}
			}
			""";
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", caller)], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	public static IEnumerable<Func<CallerConstructCase>> SynchronousCallers()
	{
		yield return () => new("expression lambda", "Func<int> next = () => Counter.CountUp();", "next()");
		yield return () => new("statement lambda", "Func<int> next = () => { return Counter.CountUp(); };", "next()");
		yield return () => new("local function", "int Next() => Counter.CountUp();", "Next()");
		yield return () => new("static local function", "static int Next() => Counter.CountUp();", "Next()");
		yield return () => new("generic caller", "static int Next<T>() => Counter.CountUp();", "Next<string>()");
		yield return () => new("iterator", "static IEnumerable<int> Next() { yield return Counter.CountUp(); }", "System.Linq.Enumerable.Single(Next())");
	}

	public sealed record CallerConstructCase(string Name, string Declaration, string Expression)
	{
		public override string ToString() => Name;
	}
}
