using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests;

public sealed class RuntimeArgumentTests
{
	[Test]
	[MethodDataSource(nameof(NonconstantArguments))]
	public async Task NonconstantArgumentsReportAWarningAtTheCallSite(string argument)
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", $$"""
			using ConstExpr;

			public static class Counter
			{
				public static readonly int Input = 10;
				public static int Property => 10;
				public static int GetInput() => throw new System.InvalidOperationException("Argument must not run during compilation.");

				[ConstExpr]
				public static int CountUp(int value) => throw new System.InvalidOperationException("Method must not run during compilation.");

				public static int Run(int value) => {|#0:CountUp({{argument}})|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.CountUp(int)"));

		await test.RunAsync();
	}

	[Test]
	public async Task CallsWithNonconstantArgumentsExecuteTheOriginalMethodAtRuntime()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int value) => value + ++counter;

				public static ExecutionResult Run()
				{
					int value = 10;
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = CountUp(value);
					}
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 11, 12, 13 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}

	[Test]
	public async Task MixedCallsPrecomputeConstantsAndPreserveRuntimeArgumentSideEffects()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int value) => value + ++counter;

				public static ExecutionResult Run()
				{
					int value = 10;
					int[] values = [CountUp(10), CountUp(++value), CountUp(10), CountUp(++value)];
					return new ExecutionResult(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 11, 12, 11, 14 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(2);
		}
	}

	public static IEnumerable<string> NonconstantArguments() => ["value", "Input", "Property", "GetInput()"];
}
