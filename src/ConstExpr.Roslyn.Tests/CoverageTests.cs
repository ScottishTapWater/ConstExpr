using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class CoverageTests
{
	[Test]
	public async Task EveryCallSiteInTheSameMethodUsesAPrecomputedResult()
	{
		const string source = """
			using ConstExpr;

			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				[ConstExpr]
				public static int CountUp() => ++counter;

				public static int[] Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = CountUp() + CountUp();
					}
					return values;
				}
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));
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
			new("Counter.cs", FixtureSources.Counter), new("First.cs", first), new("Second.cs", second),
			new("Caller.cs", FixtureSources.Caller("First.Next() + Second.Next()")));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { values[0], values[0], values[0] });
		await Assert.That(fixture.Calls).IsEqualTo(0);
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
		CSharpCompilation input = await CompilationFixture.Create(
			new("Counter.cs", FixtureSources.Counter), new("OtherCounter.cs", other),
			new("Caller.cs", FixtureSources.Caller("Counter.CountUp() + OtherCounter.CountUp()")));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 2, 2, 2 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
		await Assert.That(fixture.CallsFor("Fixture.OtherCounter")).IsEqualTo(0);
	}

	[Test]
	public async Task CallsToUnmarkedMethodsExecuteTheOriginalMethodAtRuntime()
	{
		const string source = """
			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				public static int CountUp() => ++counter;

				public static int[] Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = CountUp();
					}
					return values;
				}
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(fixture.Calls).IsEqualTo(3);
	}
}
