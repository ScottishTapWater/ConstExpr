using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class CallerDependencyTests
{
	[Test]
	[MethodDataSource(nameof(CallerTypeDependencies))]
	public async Task CallerTypeDependenciesWarnWithoutExecutingTheMarkedMethod(CallerTypeDependencyTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			public class Generic<T> { public enum Value { First } }
			public interface IFactory<T> where T : IFactory<T> { static abstract int Create(); }
			public static class Counter
			{
				[ConstExpr] public static int Calculate(object input) => throw new System.Exception("The target must not execute.");
				public static unsafe int Read<T>() {{testCase.Constraints}} => Calculate({{testCase.Expression}});
			}
			""";
		CSharpCompilation input = await CompilationFixture.CreateWithOptions([new("Program.cs", source)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

		GeneratorRun run = GeneratorRun.Create(input);

		using (Assert.Multiple())
		{
			await Assert.That(run.Result.Diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo(new[] { "CONSTEXPR002" }, CollectionOrdering.Matching);
			await Assert.That(run.HasInterceptor).IsFalse();
		}
	}

	[Test]
	public async Task ImplicitDynamicReceiversWarnWithoutExecutingTheMarkedMethod()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static string ToString(int value) => value.ToString();
				[ConstExpr] public static string Calculate(string input) => throw new System.Exception("The target must not execute.");
				public static string Read<T>() => Calculate((string)ToString((dynamic)42));
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		GeneratorRun run = GeneratorRun.Create(input);

		using (Assert.Multiple())
		{
			await Assert.That(run.Result.Diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo(new[] { "CONSTEXPR002" }, CollectionOrdering.Matching);
			await Assert.That(run.HasInterceptor).IsFalse();
		}
	}

	[Test]
	public async Task CapturedQueryRangeVariablesWarnWithoutExecutingTheMarkedMethod()
	{
		const string source = """
			using ConstExpr;
			using System.Linq;
			public static class Counter
			{
				[ConstExpr] public static int Calculate(int input) => throw new System.Exception("The target must not execute.");
				public static int Run() => (from value in new[] { 42 } select Calculate(value)).Single();
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		GeneratorRun run = GeneratorRun.Create(input);

		using (Assert.Multiple())
		{
			await Assert.That(run.Result.Diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo(new[] { "CONSTEXPR002" }, CollectionOrdering.Matching);
			await Assert.That(run.HasInterceptor).IsFalse();
		}
	}

	[Test]
	[MethodDataSource(nameof(IndependentExpressions))]
	public async Task ExpressionsWithoutFreeCallerDependenciesArePrecomputed(IndependentExpressionTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using System;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				private static int Select(byte value) => value;
				private static int Select(int value) => 0;
				[ConstExpr] public static int Calculate(int input) { calls++; return input; }
				private static int Read<T>(int parameter) { {{testCase.Body}} }
				public static ExecutionResult Run() => new(new[] { Read<int>(0) }, calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run",
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	public static IEnumerable<Func<CallerTypeDependencyTestCase>> CallerTypeDependencies() =>
	[
		() => new("Type test", "new object() is T", ""),
		() => new("Recursive pattern", "new object() is T { }", ""),
		() => new("Default caller type", "default(T)", ""),
		() => new("Static abstract dispatch", "T.Create()", "where T : IFactory<T>"),
		() => new("Declaration pattern", "new object() is T value", ""),
		() => new("Sizeof", "sizeof(T)", "where T : unmanaged"),
		() => new("Typeof", "typeof(T)", ""),
		() => new("Pointer type", "typeof(T*)", "where T : unmanaged"),
		() => new("Function pointer type", "typeof(delegate*<T, void>)", "where T : unmanaged"),
		() => new("Boxed generic enum constant", "Generic<T>.Value.First", "")
	];

	public static IEnumerable<Func<IndependentExpressionTestCase>> IndependentExpressions() =>
	[
		() => new("Caller type parameter name", "return Calculate(nameof(T).Length + 41);"),
		() => new("Caller parameter name", "return Calculate(nameof(parameter).Length + 33);"),
		() => new("Typed local constant", "const byte input = 42; return Calculate(Select(input));"),
		() => new("Internal generic function", "return Calculate(((Func<int>)(() => { static U Identity<U>(U value) => value; return Identity(42); }))());"),
		() => new("Internal generic sizeof", "return Calculate(((Func<int>)(() => { static unsafe int Size<U>() where U : unmanaged => sizeof(U); return Size<int>() + 38; }))());")
	];

	public sealed record CallerTypeDependencyTestCase(string Name, string Expression, string Constraints)
	{
		public override string ToString() => Name;
	}

	public sealed record IndependentExpressionTestCase(string Name, string Body)
	{
		public override string ToString() => Name;
	}
}
