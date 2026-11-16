using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class MultipleConstantArgumentTests
{
	[Test]
	[MethodDataSource(nameof(ConstantArgumentLists))]
	public async Task MultipleConstantArgumentsAreBoundToTheirParametersAndPrecomputed(ConstantArgumentListTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int Encode(int left, int middle, int right)
				{
					counter++;
					return left * 100 + middle * 10 + right;
				}

				public static ExecutionResult Run()
				{
					{{testCase.Declarations}}
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Encode({{testCase.Arguments}});
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
	public async Task ChangingAnyArgumentOrItsPositionProducesAnIndependentPrecomputedResult()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int Encode(int left, int middle, int right)
				{
					counter++;
					return left * 100 + middle * 10 + right;
				}

				public static ExecutionResult Run()
				{
					int[] values = [Encode(1, 2, 3), Encode(4, 2, 3), Encode(1, 5, 3), Encode(1, 2, 6), Encode(3, 2, 1)];
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 123, 423, 153, 126, 321 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task EquivalentArgumentListsAcrossCallSitesEvaluateTheMethodOnce()
	{
		const string source = """
			using ConstExpr;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int left, int right) => left + right + ++counter;

				public static int[] Run() => [CountUp(10, 20), CountUp(right: 20, left: 10), CountUp(5 + 5, 4 * 5)];
			}
			""";

		int[] results = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(results).IsEquivalentTo(new[] { 31, 31, 31 });
	}

	[Test]
	public async Task OverloadsWithDifferentParameterCountsAreEvaluatedSeparately()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Calculator
			{
				private static int counter;

				[ConstExpr]
				public static int Encode(int left, int right)
				{
					counter++;
					return left * 10 + right;
				}

				[ConstExpr]
				public static int Encode(int left, int middle, int right)
				{
					counter++;
					return left * 100 + middle * 10 + right;
				}

				public static ExecutionResult Run()
				{
					int[] values = [Encode(1, 2), Encode(1, 2, 3)];
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Calculator.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 12, 123 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task EvaluationFailureWithMultipleConstantArgumentsReportsABuildError()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;

			public static class Calculator
			{
				[ConstExpr]
				public static int Calculate(int left, int right) => throw new System.InvalidOperationException("Evaluation failed.");

				public static int Run() => {|#0:Calculate(1, 2)|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Evaluation failed."));

		await test.RunAsync();
	}

	public static IEnumerable<Func<ConstantArgumentListTestCase>> ConstantArgumentLists()
	{
		yield return () => new("Positional", "1, 2, 3", 123);
		yield return () => new("Named", "left: 1, middle: 2, right: 3", 123);
		yield return () => new("Reordered named", "right: 3, left: 1, middle: 2", 123);
		yield return () => new("Mixed positional and named", "1, right: 3, middle: 2", 123);
		yield return () => new("Constants", "Input, Input + 1, Input + 2", 123, "const int Input = 1;");
		yield return () => new("Constant expressions", "1 + 1, 3 * 2, 10 / 2", 265);
		yield return () => new("Implicit numeric conversions", "(byte)1, (short)2, 3", 123);
	}

	public sealed record ConstantArgumentListTestCase(string Name, string Arguments, int ExpectedValue, string Declarations = "")
	{
		public override string ToString() => Name;
	}
}
