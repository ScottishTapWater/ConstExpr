using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class ConstantArgumentTests
{
	[Test]
	[MethodDataSource(nameof(ConstantArguments))]
	public async Task CallsWithConstantIntegerArgumentsReturnPrecomputedResults(ConstantArgumentTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int AddOne(int value)
				{
					counter++;
					return value + 1;
				}

				public static ExecutionResult Run()
				{
					{{testCase.Declarations}}
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = AddOne({{testCase.Argument}});
					}
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { testCase.ExpectedValue, testCase.ExpectedValue, testCase.ExpectedValue });
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task DifferentArgumentsToTheSameMethodProduceTheirRespectiveResults()
	{
		const string source = """
			using ConstExpr;

			public static class Calculator
			{
				[ConstExpr]
				public static int Square(int value) => value * value;

				public static int[] Run() => [Square(2), Square(3), Square(2)];
			}
			""";

		int[] results = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Calculator.Run");

		await Assert.That(results).IsEquivalentTo(new[] { 4, 9, 4 });
	}

	[Test]
	public async Task MultipleCallSitesWithTheSameArgumentEvaluateTheMethodOnce()
	{
		const string source = """
			using ConstExpr;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int value) => value + ++counter;

				public static int[] Run() => [CountUp(10), CountUp(5 + 5), CountUp(value: 10)];
			}
			""";

		int[] results = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(results).IsEquivalentTo(new[] { 11, 11, 11 });
	}

	[Test]
	public async Task ParameterlessAndIntegerOverloadsAreEvaluatedSeparately()
	{
		const string source = """
			using ConstExpr;

			public static class Calculator
			{
				[ConstExpr]
				public static int GetValue() => 7;

				[ConstExpr]
				public static int GetValue(int value) => value + 1;

				public static int[] Run() => [GetValue(), GetValue(0), GetValue(4)];
			}
			""";

		int[] results = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Calculator.Run");

		await Assert.That(results).IsEquivalentTo(new[] { 7, 1, 5 });
	}

	public static IEnumerable<Func<ConstantArgumentTestCase>> ConstantArguments()
	{
		yield return () => new("Literal", "41", 42);
		yield return () => new("Constant local", "Input", 42, "const int Input = 41;");
		yield return () => new("Constant expression", "20 * 2 + 1", 42);
		yield return () => new("Implicit numeric conversion", "(byte)41", 42);
		yield return () => new("Named argument", "value: 41", 42);
		yield return () => new("Negative value", "-2", -1);
		yield return () => new("Minimum integer", "int.MinValue", int.MinValue + 1);
	}

	public sealed record ConstantArgumentTestCase(string Name, string Argument, int ExpectedValue, string Declarations = "")
	{
		public override string ToString() => Name;
	}
}
