using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class GenericExtractionRegressionTests
{
	[Test]
	public async Task AnUninstantiatedConstrainedGenericCallerIsPrecomputed()
	{
		const string source = """
			using ConstExpr;
			using System.Collections.Generic;
			public static class Counter
			{
				private static int Create() => 42;
				[ConstExpr] public static int Calculate(int input) => input;
				public static int Read<T>() where T : IList<int>, new() => Calculate(Create());
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		GeneratorRun run = GeneratorRun.Create(input);

		await Assert.That(run.HasInterceptor).IsTrue();
	}

	[Test]
	public async Task AnInferredConstrainedCallerIsPrecomputed()
	{
		const string source = """
			using ConstExpr;
			using System.IO;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				private static int Create() => 42;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				private static int Read<T>(T stream) where T : Stream, new() => Calculate(Create());
				public static ExecutionResult Run() => new(new[] { Read(new MemoryStream()) }, calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	[Arguments(20)]
	[Arguments(40)]
	public async Task DependentConstraintsDoNotRequireSearchingUnrelatedTypes(int unrelatedTypes)
	{
		string declarations = string.Join("\n", Enumerable.Range(0, unrelatedTypes).Select(index => $"public sealed class Unrelated{index} {{ }}"));
		string source = $$"""
			using ConstExpr;
			using System.Collections.Generic;
			public static class Counter
			{
				private static int Create() => 42;
				[ConstExpr] public static int Calculate(int input) => input;
				public static int Read<T, U, V, W>() where T : IEnumerable<U> where U : IEnumerable<V> where V : IEnumerable<W> => Calculate(Create());
			}
			{{declarations}}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		GeneratorRun run = GeneratorRun.Create(input);

		await Assert.That(run.HasInterceptor).IsTrue();
	}

	[Test]
	[MethodDataSource(nameof(GenericExpressions))]
	public async Task GenericExtractionPreservesBindingAndCallerInformation(GenericExpressionTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System;
			using System.Collections.Generic;
			using System.Linq.Expressions;
			using System.Runtime.CompilerServices;
			public class Base { protected static string Inherited() => "base"; }
			public class Counter : Base
			{
				private static int calls;
				private static string Describe(string value = "", [CallerMemberName] string caller = "", [CallerArgumentExpression("value")] string expression = "") => caller + ":" + value + ":" + expression;
				private static string Line([CallerLineNumber] int line = 0) => line.ToString();
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				{{testCase.Declarations}}
				private static unsafe string Read<T>() where T : IList<int>, new()
				{
					{{testCase.LocalDeclarations}}
					#line 123 "Original.cs"
					return Calculate({{testCase.Expression}});
				}
				public static GenericExtractionRegressionTests.ExtractionResult Run() => new(Read<List<int>>(), calls);
			}
			""";

		ExtractionResult result = await ConstExprFixture.CompileAndRun<ExtractionResult>([new("Program.cs", source)], "Counter.Run",
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

		await Assert.That(result).IsEqualTo(new ExtractionResult(testCase.Expected, 0));
	}

	[Test]
	[MethodDataSource(nameof(GenericScopes))]
	public async Task GenericExtractionPreservesEnclosingLambdaScopes(GenericScopeTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System;
			using System.Collections.Generic;
			using System.Linq.Expressions;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				private static int calls;
				private static string Describe(string value, [CallerMemberName] string caller = "") => caller + ":" + value;
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				private static string Read<T>() where T : IList<int>, new() { {{testCase.Body}} }
				public static GenericExtractionRegressionTests.ExtractionResult Run() => new(Read<List<int>>(), calls);
			}
			""";

		ExtractionResult result = await ConstExprFixture.CompileAndRun<ExtractionResult>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new ExtractionResult("Read:parameter", 0));
	}

	[Test]
	public async Task AwaitedIndependentInputsInGenericCallersArePrecomputed()
	{
		const string source = """
			using ConstExpr;
			using System.IO;
			using System.Threading.Tasks;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				private static async Task<int> Read<T>() where T : Stream, new() => Calculate(await Task.FromResult(42));
				public static async Task<ExecutionResult> Run() => new(new[] { await Read<MemoryStream>() }, calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	[MethodDataSource(nameof(CommentedCallers))]
	public async Task LeadingCommentsDoNotShiftCallerLineNumbers(CommentedCallerTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				private static int calls;
				private static int Line([CallerLineNumber] int line = 0) => line;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				#line 123 "Original.cs"
				{{testCase.Declaration}} =>
				// This leading comment is part of the source location regression.
				Calculate(Line());
				public static ExecutionResult Run() => new(new[] { {{testCase.Invocation}} }, calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 125 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task GenericExtractionPreservesScopedWarningSuppression()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				[System.Obsolete("Only this caller permits the obsolete factory.")]
				private static int Create() => 42;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				#pragma warning disable CS0618
				private static int Read<T>() => Calculate(Create());
				#pragma warning restore CS0618
				public static ExecutionResult Run() => new(new[] { Read<int>() }, calls);
			}
			""";
		CSharpCompilationOptions options = new(OutputKind.DynamicallyLinkedLibrary, generalDiagnosticOption: ReportDiagnostic.Error,
			specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CONSTEXPR004"] = ReportDiagnostic.Warn });

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run", options);

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task GenericExtractionPreservesTheCallersNullableContext()
	{
		const string source = """
			#nullable enable
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static string Calculate(Func<string> input) { calls++; return input(); }
				private static string Read<T>() => Calculate(() => { string value = "value"; return value; });
				public static GenericExtractionRegressionTests.ExtractionResult Run() => new(Read<int>(), calls);
				#nullable disable
			}
			""";

		ExtractionResult result = await ConstExprFixture.CompileAndRun<ExtractionResult>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new ExtractionResult("value", 0));
	}

	[Test]
	public async Task NullableConstantInputsPreserveTheirTypeAndCallerArgumentText()
	{
		const string source = """
			#nullable enable
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				private static int calls;
				private static string Describe(string? value, [CallerArgumentExpression("value")] string expression = "") => (value ?? "null") + ":" + expression;
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				private static string Read<T>() { const string? input = null; return Calculate(Describe(input)); }
				public static GenericExtractionRegressionTests.ExtractionResult Run() => new(Read<int>(), calls);
			}
			""";

		ExtractionResult result = await ConstExprFixture.CompileAndRun<ExtractionResult>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new ExtractionResult("null:input", 0));
	}

	[Test]
	[MethodDataSource(nameof(GenericLocalContexts))]
	public async Task GenericLocalFunctionsPreserveSpecialMemberCallerNames(GenericLocalContextTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;
			using System.Collections.Generic;
			using System.Runtime.CompilerServices;
			public class Counter
			{
				private static int calls;
				private static string Describe([CallerMemberName] string caller = "") => caller;
				[ConstExpr] public static string Calculate(string input) { calls++; return input; }
				{{testCase.Declarations}}
				public static GenericExtractionRegressionTests.ExtractionResult Run() => new({{testCase.Expression}}, calls);
			}
			""";

		ExtractionResult result = await ConstExprFixture.CompileAndRun<ExtractionResult>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEqualTo(new ExtractionResult(testCase.Expected, 0));
	}

	public static IEnumerable<Func<GenericExpressionTestCase>> GenericExpressions() =>
	[
		() => new("Caller member", "", "Describe()", "Read::"),
		() => new("Caller type parameter name", "", "Describe(nameof(T))", "Read:T:nameof(T)"),
		() => new("Constrained type member name", "private class Constrained<U> where U : IList<int>, new() { public const int Member = 0; }", "Describe(nameof(Constrained<T>.Member))", "Read:Member:nameof(Constrained<T>.Member)"),
		() => new("Same named outer method", "private static string Read(int value) => value.ToString();", "Describe(Read(42))", "Read:42:Read(42)"),
		() => new("Type with object member name", "private class ToString { public static int Create() => 42; }", "ToString.Create().ToString()", "42"),
		() => new("Object member name", "private static string ToString(int value) => value.ToString();", "Describe(ToString(42))", "Read:42:ToString(42)"),
		() => new("Inherited protected member", "", "Describe(Inherited())", "Read:base:Inherited()"),
		() => new("Private nested member", "private class Input { public static string Create() => \"private\"; }", "Describe(Input.Create())", "Read:private:Input.Create()"),
		() => new("Competing optional overload", "private static string Create(object value, [CallerMemberName] string caller = \"\") => \"correct:\" + caller; private static string Create(string value, string caller) => \"wrong:\" + caller;", "Create(\"value\")", "correct:Read"),
		() => new("Indexer caller member", "private class Input { public string this[int index, [CallerMemberName] string caller = \"\"] => caller; }", "new Input()[0]", "Read"),
		() => new("Collection competing overload", "private class Input : List<string> { public void Add(int value, [CallerMemberName] string caller = \"\", params int[] rest) => base.Add(caller + \":\" + rest.Length); public void Add(int value, string caller) => base.Add(\"wrong\"); }", "new Input { 42 }[0]", "Read:0"),
		() => new("Caller line", "", "Line()", "123"),
		() => new("Directive within constant", "", "Describe(nameof(\n#line 700 \"Moved.cs\"\nT)) + \":\" + Line()", "Read:T:nameof(\n#line 700 \"Moved.cs\"\nT):700"),
		() => new("Anonymous property read", "", "new { Value = 42 }.Value.ToString()", "42"),
		() => new("Nested delegate", "", "((Func<string>)(() => Describe(nameof(T))))()", "Read:T:nameof(T)"),
		() => new("Quoted argument", "private static string Consume(Expression<Func<string>> expression) => expression.Compile()();", "Consume(() => Describe(nameof(T)))", "Read:T:nameof(T)"),
		() => new("True constant value", "private static string Select(bool input) => input ? \"true\" : \"false\";", "Select(input)", "true", "const bool input = true;"),
		() => new("False constant value", "private static string Select(bool input) => input ? \"true\" : \"false\";", "Select(input)", "false", "const bool input = false;"),
		() => new("Byte constant overload", "private static string Select(byte input) => \"byte\"; private static string Select(int input) => \"int\";", "Select(input)", "byte", "const byte input = 42;"),
		() => new("Enum constant overload", "private enum Choice { Answer = 42 } private static string Select(Choice input) => \"enum\"; private static string Select(int input) => \"int\";", "Select(input)", "enum", "const Choice input = Choice.Answer;"),
		() => new("Constant declaration overflow context", "", "Math.Abs(input).ToString()", "1", "const int input = unchecked((int)uint.MaxValue);"),
		() => new("Unsafe caller", "private struct Input { public int Value; }", "sizeof(Input).ToString()", "4")
	];

	public static IEnumerable<Func<GenericScopeTestCase>> GenericScopes() =>
	[
		() => new("Delegate", "Func<int, string> next = parameter => Calculate(Describe(nameof(parameter))); return next(42);"),
		() => new("Expression tree", "Expression<Func<int, string>> next = parameter => Calculate(Describe(nameof(parameter))); return next.Compile()(42);"),
		() => new("Nested expression trees", "Expression<Func<Expression<Func<int, string>>>> next = () => parameter => Calculate(Describe(nameof(parameter))); return next.Compile()().Compile()(42);")
	];

	public static IEnumerable<Func<GenericLocalContextTestCase>> GenericLocalContexts() =>
	[
		() => new("Constructor", "private readonly string value; public Counter() { static string Read<T>() where T : IList<int>, new() => Calculate(Describe()); value = Read<List<int>>(); }", "new Counter().value", ".ctor"),
		() => new("Accessor", "private static string Value { get { static string Read<T>() where T : IList<int>, new() => Calculate(Describe()); return Read<List<int>>(); } }", "Value", "Value"),
		() => new("Indexer", "private string this[int index] { get { static string Read<T>() where T : IList<int>, new() => Calculate(Describe()); return Read<List<int>>(); } }", "new Counter()[0]", "Item"),
		() => new("Named indexer", "[IndexerName(\"Entry\")] private string this[int index] { get { static string Read<T>() where T : IList<int>, new() => Calculate(Describe()); return Read<List<int>>(); } }", "new Counter()[0]", "Entry"),
		() => new("Operator", "public static string operator +(Counter left, int right) { static string Read<T>() where T : IList<int>, new() => Calculate(Describe()); return Read<List<int>>(); }", "new Counter() + 0", "op_Addition")
	];

	public static IEnumerable<Func<CommentedCallerTestCase>> CommentedCallers() =>
	[
		() => new("Generic caller", "private static int Read<T>()", "Read<int>()"),
		() => new("Nongeneric caller", "private static int Read()", "Read()")
	];

	public sealed record GenericExpressionTestCase(string Name, string Declarations, string Expression, string Expected, string LocalDeclarations = "")
	{
		public override string ToString() => Name;
	}

	public sealed record GenericScopeTestCase(string Name, string Body)
	{
		public override string ToString() => Name;
	}

	public sealed record GenericLocalContextTestCase(string Name, string Declarations, string Expression, string Expected)
	{
		public override string ToString() => Name;
	}

	public sealed record CommentedCallerTestCase(string Name, string Declaration, string Invocation)
	{
		public override string ToString() => Name;
	}

	public sealed record ExtractionResult(string Value, int RuntimeCalls);
}
