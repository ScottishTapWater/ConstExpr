using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests;

public sealed class SharedResultTests
{
	[Test]
	[MethodDataSource(nameof(Results))]
	public async Task ResultsAreReconstructedWithoutExecutingTheOriginalMethodAtRuntime(ResultTestCase testCase)
	{
		string source = $$"""
			#nullable enable
			using ConstExpr;
			using System.Collections.Generic;

			public sealed class Data
			{
				public string Name { get; set; } = "";
				public List<int> Values { get; set; } = [];
			}

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static {{testCase.Type}} Create(int key)
				{
					counter++;
					return {{testCase.Value}};
				}

				public static bool Run()
				{
					{{testCase.Type}} result = Create(3);
					return ({{testCase.Condition}}) && counter == 0;
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task EquivalentCallsReturnTheSameDeserialisedInstance()
	{
		const string source = """
			using ConstExpr;

			public static class Factory
			{
				[ConstExpr]
				public static int[] Create(int key) => [key];

				public static bool Run() => object.ReferenceEquals(Create(3), Create(key: 1 + 2));
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task MutationsAreVisibleThroughSubsequentCallsForTheSameArguments()
	{
		const string source = """
			using ConstExpr;
			using System.Collections.Generic;

			public static class Factory
			{
				[ConstExpr]
				public static List<int> Create(int key) => [key];

				public static bool Run()
				{
					Create(3).Add(42);
					return Create(3).Contains(42);
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task DifferentArgumentCombinationsHaveIndependentInstancesEvenWhenTheirValuesMatch()
	{
		const string source = """
			using ConstExpr;

			public static class Factory
			{
				[ConstExpr]
				public static int[] Create(int key) => [7];

				public static bool Run()
				{
					int[] first = Create(1);
					first[0] = 42;
					return Create(1)[0] == 42 && Create(2)[0] == 7;
				}
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task SerialisationFailureProducesABuildError()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;

			public sealed class Data
			{
				public int Value => throw new System.InvalidOperationException("Serialisation failed.");
			}

			public static class Factory
			{
				[ConstExpr]
				public static Data Create() => new();

				public static Data Run() => {|#0:Create()|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR001", DiagnosticSeverity.Error)
			.WithLocation(0).WithArguments("Serialisation failed."));

		await test.RunAsync();
	}

	public static IEnumerable<Func<ResultTestCase>> Results()
	{
		yield return () => new("Array", "int[]", "[key, key + 1]", "result.Length == 2 && result[0] == 3 && result[1] == 4");
		yield return () => new("List", "List<int>", "[key, key + 1]", "result.Count == 2 && result[0] == 3 && result[1] == 4");
		yield return () => new("Dictionary", "Dictionary<string, int>", "new() { [\"value\"] = key }", "result[\"value\"] == 3");
		yield return () => new("DTO", "Data", "new() { Name = \"hello \\\"world\\\"\\n\", Values = [key] }", "result.Name == \"hello \\\"world\\\"\\n\" && result.Values[0] == 3");
		yield return () => new("Null", "Data?", "null", "result is null");
	}

	public sealed record ResultTestCase(string Name, string Type, string Value, string Condition)
	{
		public override string ToString() => Name;
	}
}
