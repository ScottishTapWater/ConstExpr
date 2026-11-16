using ConstExpr.Roslyn.Tests.Infrastructure;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class GenericCallerExtractionTests
{
	[Test]
	[MethodDataSource(nameof(GenericCallers))]
	public async Task InputsIndependentOfCallerTypeParametersArePrecomputed(GenericCallerTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public interface IInput { }
			public sealed class Input : IInput { }
			public static class Counter
			{
				private static int calls;
				private static int Create(int value = 42) => value;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				{{testCase.Declaration}}
				public static ExecutionResult Run()
				{
					int[] values = [Read<{{testCase.TypeArguments}}>(), Read<{{testCase.TypeArguments}}>(), Read<{{testCase.TypeArguments}}>()];
					return new(values, calls);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42, 42, 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	public static IEnumerable<Func<GenericCallerTestCase>> GenericCallers() =>
	[
		() => new("Unconstrained", "private static int Read<T>() => Calculate(Create());", "int"),
		() => new("Value type constraint", "private static int Read<T>() where T : struct => Calculate(Create());", "int"),
		() => new("Interface and constructor constraints", "private static int Read<T>() where T : IInput, new() => Calculate(Create());", "Input"),
		() => new("Constructed framework type constraint", "private static int Read<T>() where T : System.Collections.Generic.IList<int>, new() => Calculate(Create());", "System.Collections.Generic.List<int>"),
		() => new("Transitive generic caller", "private static int Read<T>() where T : System.Collections.Generic.IList<int>, new() => Forward<T>(); private static int Forward<T>() where T : System.Collections.Generic.IList<int>, new() => Calculate(Create());", "System.Collections.Generic.List<int>"),
		() => new("Dependent constraints", "private static int Read<T>() where T : System.IComparable<T>, new() => Calculate(Create());", "int"),
		() => new("Caller type parameter name", "private static int Read<T>() => Calculate(Create(nameof(T).Length + 41));", "int"),
		() => new("Constraints between caller parameters", "private static int Read<T, U>() where T : U where U : IInput, new() => Calculate(Create());", "Input, Input"),
		() => new("Local constant", "private static int Read<T>() { const int value = 42; return Calculate(Create(value)); }", "int"),
		() => new("Unrelated generic local", "private static int Read<T>() { T unused = default; return Calculate(Create()); }", "int")
	];

	public sealed record GenericCallerTestCase(string Name, string Declaration, string TypeArguments)
	{
		public override string ToString() => Name;
	}
}
