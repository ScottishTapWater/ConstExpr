using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class IndirectCallTests
{
	[Test]
	public async Task DynamicallyBoundCallsExecuteTheOriginalMethodAtRuntime()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(int value) => ++counter;

				public static ExecutionResult Run()
				{
					dynamic value = 0;
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = Counter.CountUp(value);
					}
					return new ExecutionResult(values, counter);
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
	public async Task ReflectionInvocationsExecuteTheOriginalMethodAtRuntime()
	{
		string caller = FixtureSources.Caller("(int)method.Invoke(null, null)!",
			"System.Reflection.MethodInfo method = typeof(Counter).GetMethod(nameof(Counter.CountUp))!;");

		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", caller)], "Fixture.Caller.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}

	[Test]
	public async Task FunctionPointerInvocationsExecuteTheOriginalMethodAtRuntime()
	{
		const string declaration = """
			static unsafe int Next()
			{
				delegate*<int> method = &Counter.CountUp;
				return method();
			}
			""";
		string caller = FixtureSources.Caller("Next()", declaration);
		CSharpCompilationOptions options = new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

		ExecutionResult result = await ConstExprFixture.CompileAndRun(
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", caller)], "Fixture.Caller.Run", options);

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}
}
