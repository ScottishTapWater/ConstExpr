using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class DirectCallTests
{
	[Test]
	[MethodDataSource(nameof(DirectCallCases))]
	public async Task DirectCallsToMarkedMethodsReturnPrecomputedResults(DirectCallTestCase testCase)
	{
		ExecutionResult result = await ConstExprFixture.CompileAndRun(testCase.Sources, testCase.EntryPoint);

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	public static IEnumerable<Func<DirectCallTestCase>> DirectCallCases()
	{
		yield return () => new("Unqualified", [new("Program.cs", FixtureSources.Simple)], "Fixture.Counter.Run");
		yield return () => new("Type qualified", [new("Program.cs", CounterCalling("Counter.CountUp()"))], "Fixture.Counter.Run");
		yield return () => new("Namespace qualified", [new("Program.cs", CounterCalling("Fixture.Counter.CountUp()"))], "Fixture.Counter.Run");
		yield return () => new("Globally qualified", [new("Program.cs", CounterCalling("global::Fixture.Counter.CountUp()"))], "Fixture.Counter.Run");
		yield return () => new("Type alias", [new("Program.cs", "using Target = Fixture.Counter;\n" + CounterCalling("Target.CountUp()"))], "Fixture.Counter.Run");
		yield return () => new("Using static",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller("CountUp()", imports: "using static Fixture.Counter;"))],
			"Fixture.Caller.Run");
		yield return () => new("Other class in the same file", [new("Program.cs", """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

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
				public static ExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Counter.CountUp();
					}
					return new ExecutionResult(values, Counter.Calls);
				}
			}
			""")], "Fixture.Caller.Run");
		yield return () => new("Other source file",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller())],
			"Fixture.Caller.Run");
	}

	private static string CounterCalling(string expression) => $$"""
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
					values[index] = {{expression}};
				}
				return new ExecutionResult(values, Calls);
			}
		}
		""";

	public sealed record DirectCallTestCase(string Name, SourceFile[] Sources, string EntryPoint)
	{
		public override string ToString() => Name;
	}
}
