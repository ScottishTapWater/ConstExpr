using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class AwaitedArgumentTests
{
	[Test]
	public async Task AwaitedInputsArePrecomputedWhileRuntimeAwaitSideEffectsStillOccur()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System.Threading.Tasks;
			public static class Counter
			{
				private static int arguments;
				private static int methods;
				private static async Task<int> Create() { await Task.Yield(); return ++arguments; }
				[ConstExpr] public static int Calculate(int input) { methods++; return input; }
				public static async Task<AwaitedArgumentTests.ArgumentExecutionResult> Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Calculate(await Create());
					}
					return new(values, arguments, methods);
				}
			}
			""";

		ArgumentExecutionResult result = await ConstExprFixture.CompileAndRun<ArgumentExecutionResult>([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 }, CollectionOrdering.Matching);
			await Assert.That(result.ArgumentCalls).IsEqualTo(3);
			await Assert.That(result.MethodCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task AwaitedInputFailuresReportTheOriginalExceptionAtTheCallSite()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			using System.Threading.Tasks;
			public static class Counter
			{
				private static async Task<int> Create() { await Task.Yield(); throw new System.InvalidOperationException("Async input failed."); }
				[ConstExpr] public static int Calculate(int input) => input;
				public static async Task<int> Run() => {|#0:Calculate(await Create())|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Async input failed."));

		await test.RunAsync();
	}

	public sealed record ArgumentExecutionResult(int[] Values, int ArgumentCalls, int MethodCalls);
}
