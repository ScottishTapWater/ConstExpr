using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class DelegateTests
{
	[Test]
	[Arguments("stored lambda", "Func<int> next = () => Counter.CountUp();", "next()")]
	[Arguments("lambda callback", "static int Invoke(Func<int> callback) => callback();", "Invoke(() => Counter.CountUp())")]
	public async Task LambdaDelegateUsesTheBakedResult(string name, string declarations, string expression)
	{
		CSharpCompilation input = await CompilationFixture.Create(("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller(expression, declarations)));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	[Arguments("stored method group", "Func<int> next = Counter.CountUp;", "next()")]
	[Arguments("method group callback", "static int Invoke(Func<int> callback) => callback();", "Invoke(Counter.CountUp)")]
	public async Task MethodGroupDelegateExecutesTheOriginalMethod(string name, string declarations, string expression)
	{
		CSharpCompilation input = await CompilationFixture.Create(("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller(expression, declarations)));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(fixture.Calls).IsEqualTo(3);
	}
}
