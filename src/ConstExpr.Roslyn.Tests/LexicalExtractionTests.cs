using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class LexicalExtractionTests
{
	[Test]
	[MethodDataSource(nameof(CallerContexts))]
	public async Task ExtractionPreservesTheOriginalLexicalCallerContext(CallerContextTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				private static int calls;
				private static string Describe(string value = "", [CallerMemberName] string caller = "") => caller + ":" + value;
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				{{testCase.Declarations}}
				public static LexicalExtractionTests.ExtractionResult Run() => new({{testCase.Expression}}, calls);
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));
		using LoadedFixture baselineFixture = new(input);
		ExtractionResult baseline = await baselineFixture.Run<ExtractionResult>("Counter.Run");
		GeneratorRun generated = GeneratorRun.Create(input);
		using LoadedFixture generatedFixture = new(generated.Compilation);

		ExtractionResult result = await generatedFixture.Run<ExtractionResult>("Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Value).IsEqualTo(baseline.Value);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	[MethodDataSource(nameof(LineMappingContexts))]
	public async Task ExtractionPreservesSourceLinesInMethodsFollowingTheCaller(CallerContextTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				[ConstExpr] public static int Calculate(int input) => input;
				{{testCase.Declarations}}
				public static int Run() => {{testCase.Expression}};
				private static int Create() => CaptureLine();
				private static int CaptureLine([CallerLineNumber] int line = 0) => line;
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));
		using LoadedFixture baselineFixture = new(input);
		int baseline = await baselineFixture.Run<int>("Counter.Run");
		GeneratorRun generated = GeneratorRun.Create(input);
		using LoadedFixture generatedFixture = new(generated.Compilation);

		int result = await generatedFixture.Run<int>("Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(generated.HasInterceptor).IsTrue();
			await Assert.That(result).IsEqualTo(baseline);
		}
	}

	public static IEnumerable<Func<CallerContextTestCase>> CallerContexts() =>
	[
		() => new("Field initialiser", "private static readonly string Value = Calculate(Describe());", "Value"),
		() => new("Property initialiser", "private static string Value { get; } = Calculate(Describe());", "Value"),
		() => new("Expression-bodied property", "private static string Value => Calculate(Describe());", "Value"),
		() => new("Expression-bodied lambda scope", "private static string Invoke() { System.Func<int, string> next = parameter => Calculate(Describe(nameof(parameter))); return next(42); }", "Invoke()"),
		() => new("Expression tree", "private static string Invoke() { System.Linq.Expressions.Expression<System.Func<string>> next = () => Calculate(Describe()); return next.Compile()(); }", "Invoke()"),
		() => new("Expression-tree parameter name", "private static string Invoke() { System.Linq.Expressions.Expression<System.Func<int, string>> next = parameter => Calculate(Describe(nameof(parameter))); return next.Compile()(42); }", "Invoke()"),
		() => new("Expression-tree field initialiser", "private static readonly System.Linq.Expressions.Expression<System.Func<string>> Value = () => Calculate(Describe());", "Value.Compile()()"),
		() => new("Expression-tree property initialiser", "private static System.Linq.Expressions.Expression<System.Func<string>> Value { get; } = () => Calculate(Describe());", "Value.Compile()()"),
		() => new("Expression-tree field parameter name", "private static readonly System.Linq.Expressions.Expression<System.Func<int, string>> Value = parameter => Calculate(Describe(nameof(parameter)));", "Value.Compile()(42)"),
		() => new("Nested expression trees", "private static readonly System.Linq.Expressions.Expression<System.Func<System.Linq.Expressions.Expression<System.Func<int, string>>>> Value = () => parameter => Calculate(Describe(nameof(parameter)));", "Value.Compile()().Compile()(42)")
	];

	public static IEnumerable<Func<CallerContextTestCase>> LineMappingContexts() =>
	[
		() => new("Expression-tree field", "private static readonly System.Linq.Expressions.Expression<System.Func<int>> Value = () => Calculate(Create());", "Value.Compile()()"),
		() => new("Expression-tree method", "private static int Read() { System.Linq.Expressions.Expression<System.Func<int>> next = () => Calculate(Create()); return next.Compile()(); }", "Read()"),
		() => new("Block body", "private static int Read() { return Calculate(Create()); }", "Read()"),
		() => new("Expression body", "private static int Read() => Calculate(Create());", "Read()"),
		() => new("Field initialiser", "private static readonly int Value = Calculate(Create());", "Value"),
		() => new("Property initialiser", "private static int Value { get; } = Calculate(Create());", "Value"),
		() => new("Expression-bodied lambda", "private static int Read() { System.Func<int> next = () => Calculate(Create()); return next(); }", "Read()")
	];

	public sealed record CallerContextTestCase(string Name, string Declarations, string Expression)
	{
		public override string ToString() => Name;
	}

	public sealed record ExtractionResult(string Value, int RuntimeCalls);
}
