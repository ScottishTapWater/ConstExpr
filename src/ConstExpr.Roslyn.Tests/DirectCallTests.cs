using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class DirectCallTests
{
	[Test]
	[MethodDataSource(nameof(DirectCallCases))]
	public async Task DirectCallsToMarkedMethodsReturnPrecomputedResults(DirectCallTestCase testCase)
	{
		CSharpCompilation input = await CompilationFixture.Create(testCase.Sources);
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, testCase.EntryType);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	public static IEnumerable<Func<DirectCallTestCase>> DirectCallCases()
	{
		yield return () => new("Unqualified", [new("Program.cs", FixtureSources.Simple)], "Fixture.Counter");
		yield return () => new("Type qualified", [new("Program.cs", CounterCalling("Counter.CountUp()"))], "Fixture.Counter");
		yield return () => new("Namespace qualified", [new("Program.cs", CounterCalling("Fixture.Counter.CountUp()"))], "Fixture.Counter");
		yield return () => new("Globally qualified", [new("Program.cs", CounterCalling("global::Fixture.Counter.CountUp()"))], "Fixture.Counter");
		yield return () => new("Type alias", [new("Program.cs", "using Target = Fixture.Counter;\n" + CounterCalling("Target.CountUp()"))], "Fixture.Counter");
		yield return () => new("Using static",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller("CountUp()", imports: "using static Fixture.Counter;"))],
			"Fixture.Caller");
		yield return () => new("Other class in the same file", [new("Program.cs", """
			using ConstExpr;

			namespace Fixture;

			public static class Counter
			{
				private static int counter;
				public static int Calls => counter;

				[ConstExpr]
				public static int CountUp() => ++counter;
			}

			public static class Caller
			{
				public static int[] Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Counter.CountUp();
					}
					return values;
				}
			}
			""")], "Fixture.Caller");
		yield return () => new("Other source file",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller())],
			"Fixture.Caller");
	}

	private static string CounterCalling(string expression) => $$"""
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
					values[index] = {{expression}};
				}
				return values;
			}
		}
		""";

	public sealed record DirectCallTestCase(string Name, SourceFile[] Sources, string EntryType)
	{
		public override string ToString() => Name;
	}
}
