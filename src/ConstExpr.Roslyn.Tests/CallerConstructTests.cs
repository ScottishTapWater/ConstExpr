using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class CallerConstructTests
{
	[Test]
	[Arguments("expression lambda", "Func<int> next = () => Counter.CountUp();", "next()")]
	[Arguments("statement lambda", "Func<int> next = () => { return Counter.CountUp(); };", "next()")]
	[Arguments("local function", "int Next() => Counter.CountUp();", "Next()")]
	[Arguments("static local function", "static int Next() => Counter.CountUp();", "Next()")]
	[Arguments("generic caller", "static int Next<T>() => Counter.CountUp();", "Next<string>()")]
	[Arguments("iterator", "static IEnumerable<int> Next() { yield return Counter.CountUp(); }", "System.Linq.Enumerable.Single(Next())")]
	[Arguments("async method", "static async Task<int> Next() { await Task.Yield(); return Counter.CountUp(); }", "await Next()")]
	public async Task CallInsideConstructUsesTheBakedResult(string name, string declarations, string expression)
	{
		string caller = FixtureSources.Caller(expression, declarations);
		if (name == "async method")
		{
			caller = caller.Replace("public static int[] Run()", "public static async Task<int[]> Run()", StringComparison.Ordinal);
		}
		CSharpCompilation input = await CompilationFixture.Create(("Counter.cs", FixtureSources.Counter), ("Caller.cs", caller));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}
}
