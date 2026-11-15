using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests;

public sealed class ExpressionScopeRegressionTests
{
	[Test]
	[MethodDataSource(nameof(CallerInformationExpressions))]
	public async Task ImplicitArgumentCallsPreserveOriginalCallerInformation(CallerInformationTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using System.Collections.Generic;
			using System.Runtime.CompilerServices;
			{{testCase.Declarations}}
			public static class Counter
			{
				private static int calls;
				public static int Create() => 42;
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				#line 123 "Original.cs"
				public static string Run() => Calculate({{testCase.Expression}}) + ":" + calls;
			}
			""";

		string result = await ConstExprFixture.CompileAndRun<string>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(testCase.Expected + ":0");
	}

	[Test]
	public async Task LocalConstantsRetainTheirDeclarationOverflowContext()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				public static int Run()
				{
					unchecked
					{
						const int input = (int)uint.MaxValue;
						checked { return Calculate(System.Math.Abs(input)) + calls * 100; }
					}
				}
			}
			""";

		int result = await ConstExprFixture.CompileAndRun<int>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(1);
	}

	[Test]
	[MethodDataSource(nameof(UnsafeContexts))]
	public async Task ArgumentExpressionsRetainTheirUnsafeContext(UnsafeContextTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			public struct Input { public int Value; }
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				{{testCase.RunMethod}}
			}
			""";
		CSharpCompilationOptions options = new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

		int result = await ConstExprFixture.CompileAndRun<int>([new("Program.cs", source)], "Counter.Run", options);

		await Assert.That(result).IsEqualTo(sizeof(int));
	}

	[Test]
	public async Task QueryExpressionsCapturingCallerParametersRemainUnintercepted()
	{
		ConstExprGeneratorTest test = new();
		test.TestState.Sources.Add(("Program.cs", """
			using ConstExpr;
			using System.Linq;
			using System.Collections.Generic;
			public static class Counter
			{
				[ConstExpr] public static int Calculate(IEnumerable<int> input) => throw new System.Exception("Must not execute.");
				public static int Run(int parameter) => {|#0:Calculate(from value in new[] { 1, 2 } select value + parameter)|};
			}
			"""));
		test.ExpectedDiagnostics.Add(new DiagnosticResult("CONSTEXPR002", DiagnosticSeverity.Warning)
			.WithLocation(0).WithArguments("Counter.Calculate(System.Collections.Generic.IEnumerable<int>)"));

		await test.RunAsync();
	}

	public static IEnumerable<Func<CallerInformationTestCase>> CallerInformationExpressions() =>
	[
		() => new("Indexer member name", "public class Input { public string this[int value, [CallerMemberName] string caller = \"\"] => caller; }", "new Input()[0]", "Run"),
		() => new("Indexer line number", "public class Input { public string this[int value, [CallerLineNumber] int line = 0] => line.ToString(); }", "new Input()[0]", "123"),
		() => new("Conditional indexer member name", "public class Input { public string this[int value, [CallerMemberName] string caller = \"\"] => caller; }", "new Input()?[0]", "Run"),
		() => new("Conditional indexer line number", "public class Input { public string this[int value, [CallerLineNumber] int line = 0] => line.ToString(); }", "new Input()?[0]", "123"),
		() => new("Conditional indexer null receiver", "public class Input { public string this[int value, [CallerMemberName] string caller = \"\"] => caller; }", "((Input)null)?[0] ?? \"missing\"", "missing"),
		() => new("Collection overload with params", """
			public class Input : List<string>
			{
				public void Add(int value, [CallerMemberName] string caller = "", params int[] rest) => base.Add("correct:" + caller + ":" + rest.Length);
				public void Add(int value, string caller) => base.Add("wrong:" + caller);
			}
			""", "new Input { 1 }[0]", "correct:Run:0"),
		() => new("Collection overload with params collection", """
			public class Input : List<string>
			{
				public void Add(int value, [CallerMemberName] string caller = "", params List<int> rest) => base.Add("correct:" + caller + ":" + rest.Count);
				public void Add(int value, string caller) => base.Add("wrong:" + caller);
			}
			""", "new Input { 1 }[0]", "correct:Run:0"),
		() => new("Method overload with params", """
			public static class Input
			{
				public static string Create(int value, [CallerMemberName] string caller = "", params int[] rest) => "correct:" + caller + ":" + rest.Length;
				public static string Create(int value, string caller) => "wrong:" + caller;
			}
			""", "Input.Create(1)", "correct:Run:0"),
		() => new("Constructor overload with params", """
			public class Input
			{
				public string Caller { get; }
				public Input(int value, [CallerMemberName] string caller = "", params int[] rest) => Caller = "correct:" + caller + ":" + rest.Length;
				public Input(int value, string caller) => Caller = "wrong:" + caller;
			}
			""", "new Input(1).Caller", "correct:Run:0"),
		() => new("Indexer overload with params", """
			public class Input
			{
				public string this[int value, [CallerMemberName] string caller = "", params int[] rest] => "correct:" + caller + ":" + rest.Length;
				public string this[int value, string caller] => "wrong:" + caller;
			}
			""", "new Input()[0]", "correct:Run:0"),
		() => new("Collection member name", "public class Input : List<string> { public void Add(int value, [CallerMemberName] string caller = \"\") => base.Add(caller); }", "new Input { 1 }[0]", "Run"),
		() => new("Collection nonfinite default", "public class Input : List<string> { public void Add(int value, double padding = double.NaN, [CallerMemberName] string caller = \"\") => base.Add(caller + \":\" + double.IsNaN(padding)); }", "new Input { 1 }[0]", "Run:True"),
		() => new("Collection signed zero default", "public class Input : List<string> { public void Add(int value, double padding = -0d, [CallerMemberName] string caller = \"\") => base.Add(caller + \":\" + (System.BitConverter.DoubleToInt64Bits(padding) < 0)); }", "new Input { 1 }[0]", "Run:True"),
		() => new("Collection argument expression", "public class Input : List<string> { public void Add(int value, [CallerArgumentExpression(\"value\")] string expression = \"\") => base.Add(expression); }", "new Input { Counter.Create() }[0]", "Counter.Create()")
	];

	public static IEnumerable<Func<UnsafeContextTestCase>> UnsafeContexts() =>
	[
		() => new("Unsafe method", "public static unsafe int Run() => Calculate(sizeof(Input)) + calls * 100;"),
		() => new("Unsafe block", "public static int Run() { unsafe { return Calculate(sizeof(Input)) + calls * 100; } }")
	];

	public sealed record CallerInformationTestCase(string Name, string Declarations, string Expression, string Expected)
	{
		public override string ToString() => Name;
	}

	public sealed record UnsafeContextTestCase(string Name, string RunMethod)
	{
		public override string ToString() => Name;
	}
}
