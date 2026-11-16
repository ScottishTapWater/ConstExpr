using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class MultipleRuntimeArgumentTests
{
	[Test]
	[MethodDataSource(nameof(NonconstantArgumentLists))]
	public async Task AnyArgumentRequiringCallerRuntimeStateReportsOneWarningAtTheCallSite(NonconstantArgumentListTestCase testCase)
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", $$"""
			using ConstExpr;

			public static class Counter
			{
				public static int GetInput(int value) => throw new System.InvalidOperationException("Argument must not run during compilation.");

				[ConstExpr]
				public static int Encode(int left, int middle, int right) => throw new System.InvalidOperationException("Method must not run during compilation.");

				public static int Run(int value) => {|#0:Encode({{testCase.Arguments}})|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.Encode(int, int, int)"));

		await test.RunAsync();
	}

	[Test]
	public async Task RuntimeFallbackPreservesNamedArgumentEvaluationOrder()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;

			public static class Counter
			{
				private static int argumentCalls;
				private static int methodCalls;

				private static int Next(int value) => ++argumentCalls;

				[ConstExpr]
				public static int Encode(int left, int middle, int right)
				{
					methodCalls++;
					return left * 100 + middle * 10 + right;
				}

				public static MultipleRuntimeArgumentTests.RuntimeArgumentExecutionResult Run()
				{
					int input = 0;
					int value = Encode(right: Next(input), left: Next(input), middle: Next(input));
					return new(value, argumentCalls, methodCalls);
				}
			}
			""";

		RuntimeArgumentExecutionResult result = await ConstExprFixture.CompileAndRun<RuntimeArgumentExecutionResult>(
			[new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new RuntimeArgumentExecutionResult(231, 3, 1));
	}

	[Test]
	public async Task MixedCallsPrecomputeConstantListsAndPreserveRuntimeArgumentSideEffects()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int left, int right) => left * 10 + right + ++counter;

				public static ExecutionResult Run()
				{
					int value = 2;
					int[] values = [CountUp(1, 2), CountUp(1, ++value), CountUp(1, 2), CountUp(1, ++value)];
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 13, 14, 13, 16 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(2);
		}
	}

	public static IEnumerable<Func<NonconstantArgumentListTestCase>> NonconstantArgumentLists()
	{
		yield return () => new("First argument", "value, 2, 3");
		yield return () => new("Middle argument", "1, value, 3");
		yield return () => new("Last argument", "1, 2, value");
		yield return () => new("Every argument", "value, value, value");
		yield return () => new("Reordered named argument", "right: value, left: 1, middle: 2");
		yield return () => new("Runtime expression", "1, 2, GetInput(value)");
	}

	public sealed record NonconstantArgumentListTestCase(string Name, string Arguments)
	{
		public override string ToString() => Name;
	}

	public sealed record RuntimeArgumentExecutionResult(int Value, int ArgumentCalls, int MethodCalls);
}
