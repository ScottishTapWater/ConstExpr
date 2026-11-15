using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class DelegateTests
{
	[Test]
	[Arguments("stored lambda", "Func<int> next = () => Counter.CountUp();", "next()")]
	[Arguments("lambda callback", "static int Invoke(Func<int> callback) => callback();", "Invoke(() => Counter.CountUp())")]
	public async Task CallsInsideLambdaDelegatesUsePrecomputedResults(string name, string declarations, string expression)
	{
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller(expression, declarations))], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(result.RuntimeCalls).IsEqualTo(0);
	}

	[Test]
	[Arguments("stored method group", "Func<int> next = Counter.CountUp;", "next()")]
	[Arguments("method group callback", "static int Invoke(Func<int> callback) => callback();", "Invoke(Counter.CountUp)")]
	public async Task MethodGroupDelegatesExecuteTheOriginalMethodOnEveryInvocation(string name, string declarations, string expression)
	{
		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller(expression, declarations))], "Fixture.Caller.Run");

		await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(result.RuntimeCalls).IsEqualTo(3);
	}
}
