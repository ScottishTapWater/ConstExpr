using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class BuildTimeArgumentTests
{
	[Test]
	[MethodDataSource(nameof(ArgumentExpressions))]
	public async Task ArgumentExpressionsAreEvaluatedAtBuildTimeWithoutRunningTheMarkedMethodAtRuntime(ArgumentExpressionTestCase testCase)
	{
		string source = $$"""
			using System;
			using System.Collections.Generic;
			using System.Linq;
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			using InputAlias = Fixture.Input;

			namespace Fixture;

			public sealed class Input
			{
				private readonly int value;
				public Input(int value) => this.value = value;
				public int Read() => value;
				public int Value { get; set; }
			}

			public static class Counter
			{
				private static int counter;
				private static int Input => 42;
				private static readonly int Field = 42;
				private static int Create(int value = 42) => value;
				private static InputAlias CreateInput() => new(42);

				[ConstExpr]
				public static int Calculate({{testCase.ParameterType}} value) => ({{testCase.ReadValue}}) + ++counter;

				public static ExecutionResult Run()
				{
					const int localConstant = 21;
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Calculate({{testCase.Expression}});
					}
					return new(values, counter);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Fixture.Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 43, 43, 43 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task RuntimeArgumentSideEffectsStillOccurAndTheirChangingValuesAreIgnored()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;

			public static class Counter
			{
				private static int arguments;
				private static int methods;
				private static int Next() => ++arguments;

				[ConstExpr]
				public static int Calculate(int value)
				{
					methods++;
					return value;
				}

				public static BuildTimeArgumentTests.ArgumentExecutionResult Run()
				{
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Calculate(Next());
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
	public async Task PrecomputedCallsWarnThatArgumentExpressionsStillRunAndTheirValuesAreIgnored()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int Create() => 42;
				[ConstExpr] public static int Calculate(int value) => value;
				public static int Run() => Calculate(Create());
			}
			""";
		CSharpCompilation compilation = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		GeneratorRun run = GeneratorRun.Create(compilation);

		await Assert.That(run.Result.Diagnostics).Count().IsEqualTo(1);
		Diagnostic diagnostic = run.Result.Diagnostics.Single();
		using (Assert.Multiple())
		{
			await Assert.That(diagnostic.Id).IsEqualTo("CONSTEXPR004");
			await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
			await Assert.That(diagnostic.GetMessage()).IsEqualTo(
				"Call to 'Counter.Calculate(int)' uses a precomputed result; argument expressions still execute at runtime but their values are ignored. Move argument construction inside the [ConstExpr] method to avoid this runtime work.");
			await Assert.That(source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)).IsEqualTo("Calculate(Create())");
		}
	}

	[Test]
	public async Task ArgumentEvaluationFailuresReportTheOriginalExceptionAtTheCallSite()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			public static class Counter
			{
				private static int Create() => throw new System.InvalidOperationException("Input failed.");
				[ConstExpr] public static int Calculate(int value) => value;
				public static int Run() => {|#0:Calculate(Create())|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Input failed."));

		await test.RunAsync();
	}

	[Test]
	[MethodDataSource(nameof(CallerStateExpressions))]
	public async Task ExpressionsDependingOnCallerStateWarnAndRemainUnintercepted(string expression)
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", $$"""
			using ConstExpr;
			public class Counter
			{
				private int input = 42;
				private int Input => input;
				private int Read() => input;
				[ConstExpr] public static int Calculate(int value) => throw new System.Exception("Must not execute.");

				public int Run(int parameter)
				{
					int local = 42;
					int ReadLocal() => local;
					return {|#0:Calculate({{expression}})|};
				}
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.Calculate(int)"));

		await test.RunAsync();
	}

	[Test]
	public async Task BuildTimeArgumentEvaluationPreservesNamedArgumentOrder()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int arguments;
				private static int methods;
				private static int Next() => ++arguments;
				[ConstExpr] public static int Encode(int left, int middle, int right)
				{
					methods++;
					return left * 100 + middle * 10 + right;
				}
				public static int Run() => Encode(right: Next(), left: Next(), middle: Next()) + methods * 1000;
			}
			""";

		int result = await ConstExprFixture.CompileAndRun<int>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(231);
	}

	[Test]
	public async Task ArgumentExpressionsPreserveTheCallersCheckedContext()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			public static class Counter
			{
				private static int Create() => int.MaxValue;
				[ConstExpr] public static int Calculate(int value) => value;
				public static int Run() => checked({|#0:Calculate(Create() + 1)|});
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Arithmetic operation resulted in an overflow."));

		await test.RunAsync();
	}

	[Test]
	public async Task ArgumentEvaluationPreservesCallerInformationDefaults()
	{
		const string source = """
			using ConstExpr;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				private static int Create() => 42;
				[ConstExpr] public static string Calculate(int value, [CallerMemberName] string caller = "") => caller;
				public static string Run() => Calculate(Create());
			}
			""";

		string result = await ConstExprFixture.CompileAndRun<string>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo("Run");
	}

	[Test]
	public async Task ExpressionCallsReturnTheSameReconstructedInstanceOnRepeatedExecution()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			public sealed class Box { public int Value { get; set; } }
			public static class Counter
			{
				private static int methods;
				[ConstExpr] public static Box Calculate(Box input)
				{
					methods++;
					return input;
				}
				public static BuildTimeArgumentTests.SharedExpressionResult Run()
				{
					Box[] results = new Box[3];
					for (int index = 0; index < results.Length; index++)
					{
						results[index] = Calculate(new Box { Value = 42 });
					}
					return new(ReferenceEquals(results[0], results[1]) && ReferenceEquals(results[0], results[2]), results[0].Value, methods);
				}
			}
			""";

		SharedExpressionResult result = await ConstExprFixture.CompileAndRun<SharedExpressionResult>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new SharedExpressionResult(true, 42, 0));
	}

	[Test]
	public async Task ArgumentFactoriesDependingOnCallerTypeParametersRemainUnintercepted()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			public static class Counter
			{
				private static int Create<T>() => throw new System.Exception("Must not execute.");
				[ConstExpr] public static int Calculate(int value) => value;
				public static int Run<T>() => {|#0:Calculate(Create<T>())|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.Calculate(int)"));

		await test.RunAsync();
	}

	public static IEnumerable<string> CallerStateExpressions() =>
	[
		"parameter", "local", "input", "Input", "Read()", "this.Read()", "ReadLocal()"
	];

	public static IEnumerable<Func<ArgumentExpressionTestCase>> ArgumentExpressions() =>
	[
		() => new("Array construction", "int[]", "new[] { 20, 22 }", "value.Sum()"),
		() => new("Collection expression", "List<int>", "[20, 22]", "value.Sum()"),
		() => new("List initialiser", "IReadOnlyList<int>", "new List<int> { 20, 22 }", "value.Sum()"),
		() => new("Dictionary initialiser", "IReadOnlyDictionary<string, int>", "new Dictionary<string, int> { [\"answer\"] = 42 }", "value[\"answer\"]"),
		() => new("Private input state", "InputAlias", "new InputAlias(42)", "value.Read()"),
		() => new("Target typed construction", "InputAlias", "new(42)", "value.Read()"),
		() => new("Object initialiser", "InputAlias", "new InputAlias(0) { Value = 42 }", "value.Value"),
		() => new("Private factory", "InputAlias", "CreateInput()", "value.Read()"),
		() => new("Nested calls", "int", "Create(Create(42))", "value"),
		() => new("Static property", "int", "Input", "value"),
		() => new("Static field", "int", "Field", "value"),
		() => new("Local constant in expression", "int", "Create(localConstant * 2)", "value"),
		() => new("Lambda input", "Func<int, int>", "value => value * 2", "value(21)"),
		() => new("Named argument", "int", "value: Create(value: 42)", "value"),
		() => new("Default argument", "int", "Create()", "value"),
		() => new("External factory", "int", "Math.Abs(-42)", "value"),
		() => new("Nonfinite constant in expression", "double", "Math.Abs(double.NaN)", "double.IsNaN(value) ? 42 : 0"),
		() => new("Nameof local variable", "string", "nameof(values)", "value == \"values\" ? 42 : 0")
	];

	public sealed record ArgumentExpressionTestCase(string Name, string ParameterType, string Expression, string ReadValue)
	{
		public override string ToString() => Name;
	}

	public sealed record ArgumentExecutionResult(int[] Values, int ArgumentCalls, int MethodCalls);

	public sealed record SharedExpressionResult(bool SameInstance, int Value, int RuntimeCalls);
}
