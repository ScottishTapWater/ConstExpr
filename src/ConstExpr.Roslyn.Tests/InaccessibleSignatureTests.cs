using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class InaccessibleSignatureTests
{
	[Test]
	[MethodDataSource(nameof(InaccessibleInputs))]
	public async Task PrivateParameterTypesReportAFallbackWarningWithoutGeneratingInaccessibleCode(InaccessibleInputTestCase testCase)
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", $$"""
			using ConstExpr;
			public static class Counter
			{
				private sealed class Input { }
				[ConstExpr] private static int Calculate({{testCase.ParameterType}} input) => throw new System.Exception("Must not execute.");
				public static int Run() => {|#0:Calculate({{testCase.Expression}})|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR005", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments($"Counter.Calculate({testCase.DisplayType})"));

		await test.RunAsync();
	}

	[Test]
	public async Task CallsWithPrivateParameterTypesStillCompileAndExecuteNormallyAtRuntime()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private sealed class Input { }
				private static int calls;
				[ConstExpr] private static int Calculate(Input input) => ++calls;
				public static ExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Calculate(new Input());
					}
					return new(values, calls);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}

	[Test]
	public async Task FileLocalParameterTypesReportAFallbackWarning()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			file sealed class Input { }
			file static class Counter
			{
				[ConstExpr] private static int Calculate(Input input) => throw new System.Exception("Must not execute.");
				public static int Run() => {|#0:Calculate(new Input())|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR005", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.Calculate(Input)"));

		await test.RunAsync();
	}

	public static IEnumerable<Func<InaccessibleInputTestCase>> InaccessibleInputs() =>
	[
		() => new("Private type", "Input", "new Input()", "Counter.Input"),
		() => new("Private array element", "Input[]", "new Input[0]", "Counter.Input[]"),
		() => new("Private generic argument", "System.Collections.Generic.List<Input>", "new System.Collections.Generic.List<Input>()", "System.Collections.Generic.List<Counter.Input>")
	];

	public sealed record InaccessibleInputTestCase(string Name, string ParameterType, string Expression, string DisplayType)
	{
		public override string ToString() => Name;
	}
}
