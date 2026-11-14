using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class CoverageTests
{
	[Test]
	public async Task EveryCallSiteInTheSameMethodUsesAPrecomputedResult()
	{
		string source = FixtureSources.Simple.Replace("CountUp();", "CountUp() + CountUp();", StringComparison.Ordinal);
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", source));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { values[0], values[0], values[0] });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task CallSitesInDifferentSourceFilesUsePrecomputedResults()
	{
		const string first = "namespace Fixture; public static class First { public static int Next() => Counter.CountUp(); }";
		const string second = "namespace Fixture; public static class Second { public static int Next() => Counter.CountUp(); }";
		CSharpCompilation input = await CompilationFixture.Create(
			("Counter.cs", FixtureSources.Counter), ("First.cs", first), ("Second.cs", second),
			("Caller.cs", FixtureSources.Caller("First.Next() + Second.Next()")));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { values[0], values[0], values[0] });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToDifferentMarkedMethodsUseTheirOwnPrecomputedResults()
	{
		string other = FixtureSources.Counter.Replace("class Counter", "class OtherCounter", StringComparison.Ordinal);
		CSharpCompilation input = await CompilationFixture.Create(
			("Counter.cs", FixtureSources.Counter), ("OtherCounter.cs", other),
			("Caller.cs", FixtureSources.Caller("Counter.CountUp() + OtherCounter.CountUp()")));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 2, 2, 2 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
		await Assert.That(fixture.CallsFor("Fixture.OtherCounter")).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToUnmarkedMethodsExecuteTheOriginalMethodAtRuntime()
	{
		string source = FixtureSources.Simple.Replace("[ConstExpr]", "", StringComparison.Ordinal);
		CSharpCompilation input = await CompilationFixture.Create(("Program.cs", source));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(fixture.Calls).IsEqualTo(3);
	}
}
