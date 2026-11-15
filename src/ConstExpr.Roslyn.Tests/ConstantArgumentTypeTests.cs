using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests;

public sealed class ConstantArgumentTypeTests
{
	[Test]
	[MethodDataSource(nameof(ConstantArguments))]
	public async Task ConstantArgumentsReachBuildTimeEvaluationWithTheirDeclaredTypes(ArgumentTestCase testCase)
	{
		string source = $$"""
			#nullable enable
			using ConstExpr;

			public enum Choice : ulong { Large = 18000000000000000000UL }

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static bool Check({{testCase.Type}} value)
				{
					counter++;
					return {{testCase.Condition}};
				}

				public static bool Run() => Check({{testCase.Value}}) && counter == 0;
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task MixedArgumentTypesAreBoundByParameterRatherThanSourceOrder()
	{
		const string source = """
			using ConstExpr;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static string Describe(string text, int count, bool upper)
				{
					counter++;
					return (upper ? text.ToUpperInvariant() : text) + ":" + count;
				}

				public static bool Run() => Describe(upper: true, count: 3, text: "abc") == "ABC:3" && counter == 0;
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task EquivalentStringArgumentsShareOneBuildTimeEvaluation()
	{
		const string source = """
			using ConstExpr;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static int CountUp(string value) => value.Length + ++counter;

				public static int[] Run() => [CountUp("hello"), CountUp("hel" + "lo"), CountUp(value: "hello")];
			}
			""";

		int[] results = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(results).IsEquivalentTo(new[] { 6, 6, 6 });
	}

	[Test]
	public async Task FloatingPointResultsUseMatchingSerialisationAndDeserialisationOptions()
	{
		const string source = """
			using ConstExpr;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static double Create()
				{
					counter++;
					return double.NaN;
				}

				public static bool Run() => double.IsNaN(Create()) && counter == 0;
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	[MethodDataSource(nameof(RuntimeArgumentTypes))]
	public async Task NonconstantArgumentsOfSupportedTypesReportTheRuntimeFallbackWarning(string type)
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", $$"""
			using ConstExpr;

			public enum Choice { First }

			public static class Counter
			{
				[ConstExpr]
				public static bool Check({{type}} value) => throw new System.InvalidOperationException("Must not run during compilation.");

				public static bool Run({{type}} value) => {|#0:Check(value)|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments($"Counter.Check({type})"));

		await test.RunAsync();
	}

	public static IEnumerable<Func<ArgumentTestCase>> ConstantArguments()
	{
		yield return () => new("Boolean", "bool", "true", "value");
		yield return () => new("String", "string", "\"hello world\"", "value == \"hello world\"");
		yield return () => new("Null string", "string?", "null", "value is null");
		yield return () => new("Character", "char", "'x'", "value == 'x'");
		yield return () => new("Signed byte", "sbyte", "-7", "value == -7");
		yield return () => new("Byte", "byte", "255", "value == 255");
		yield return () => new("Short", "short", "-123", "value == -123");
		yield return () => new("Unsigned short", "ushort", "1000", "value == 1000");
		yield return () => new("Unsigned integer", "uint", "4000000000u", "value == 4000000000u");
		yield return () => new("Long", "long", "5000000000L", "value == 5000000000L");
		yield return () => new("Unsigned long", "ulong", "18000000000000000000UL", "value == 18000000000000000000UL");
		yield return () => new("Float", "float", "1.25f", "value == 1.25f");
		yield return () => new("Double", "double", "2.5d", "value == 2.5d");
		yield return () => new("Decimal", "decimal", "12.50m", "value == 12.50m");
		yield return () => new("Source enum", "Choice", "Choice.Large", "value == Choice.Large");
		yield return () => new("Framework enum", "System.DayOfWeek", "System.DayOfWeek.Friday", "value == System.DayOfWeek.Friday");
		yield return () => new("NaN", "double", "double.NaN", "double.IsNaN(value)");
		yield return () => new("Infinity", "double", "double.PositiveInfinity", "double.IsPositiveInfinity(value)");
	}

	[Test]
	[MethodDataSource(nameof(DistinctRepresentations))]
	public async Task DistinctConstantRepresentationsHaveIndependentCacheEntries(CacheTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;

			public static class Counter
			{
				private static int counter;

				[ConstExpr]
				public static long Inspect({{testCase.Type}} value)
				{
					counter++;
					return {{testCase.Body}};
				}

				public static ConstantArgumentTypeTests.CacheObservation Run() =>
					new(Inspect({{testCase.FirstArgument}}), Inspect({{testCase.SecondArgument}}), counter);
			}
			""";

		CacheObservation result = await ConstExprFixture.CompileAndRun<CacheObservation>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new CacheObservation(testCase.FirstExpected, testCase.SecondExpected, 0));
	}

	public static IEnumerable<Func<CacheTestCase>> DistinctRepresentations()
	{
		yield return () => new("UTF-16 strings", "string", "value[0]", "\"\\uD800\"", "\"\\uD801\"", 0xD800, 0xD801);
		yield return () => new("Signed zero", "double", "System.BitConverter.DoubleToInt64Bits(value)", "0.0d", "-0.0d", 0, long.MinValue);
		yield return () => new("Decimal scale", "decimal", "decimal.GetBits(value)[3]", "1m", "1.0m", 0, 65536);
	}

	public static IEnumerable<string> RuntimeArgumentTypes() => ["string", "bool", "double", "Choice"];

	public sealed record ArgumentTestCase(string Name, string Type, string Value, string Condition)
	{
		public override string ToString() => Name;
	}

	public sealed record CacheTestCase(string Name, string Type, string Body, string FirstArgument, string SecondArgument, long FirstExpected, long SecondExpected)
	{
		public override string ToString() => Name;
	}

	public sealed record CacheObservation(long First, long Second, int RuntimeCalls);
}
