using ConstExpr.Roslyn.Tests.Infrastructure;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class AsyncCallerExtractionTests
{
	[Test]
	[MethodDataSource(nameof(AsyncCallers))]
	public async Task AsyncArrowBodiesKeepTheirOriginalReturnSemantics(AsyncCallerTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			using System.Collections.Generic;
			using System.Threading.Tasks;
			public static class Counter
			{
				private static int arguments;
				private static int methods;
				private static readonly List<int> values = new();
				private static int Create() => ++arguments;
				private static Task Capture(int value) { values.Add(value); return Task.CompletedTask; }
				[ConstExpr] public static int Calculate(int input) { methods++; return input; }
				{{testCase.Declaration}}
				public static async Task<ExecutionResult> Run()
				{
					{{testCase.LocalDeclaration}}
					await Read();
					await Read();
					await Read();
					return new(values.ToArray(), methods);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 1, 1 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	public static IEnumerable<Func<AsyncCallerTestCase>> AsyncCallers() =>
	[
		() => new("Task method", "private static async Task Read() => await Capture(Calculate(Create()));", ""),
		() => new("ValueTask method", "private static async ValueTask Read() => await Capture(Calculate(Create()));", ""),
		() => new("Task local function", "", "static async Task Read() => await Capture(Calculate(Create()));")
	];

	public sealed record AsyncCallerTestCase(string Name, string Declaration, string LocalDeclaration)
	{
		public override string ToString() => Name;
	}
}
