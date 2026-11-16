using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class GenericMethodTests
{
	[Test]
	[MethodDataSource(nameof(ClosedCalls))]
	public async Task ClosedGenericCallsReturnPrecomputedResultsWithoutRuntimeExecution(GenericCallTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			using System.Collections.Generic;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public sealed class Input { public int Value { get; set; } = 42; }
			public static class Counter
			{
				private static int calls;
				{{testCase.Declarations}}
				public static ExecutionResult Run() => new([{{testCase.Call}}, {{testCase.Call}}], calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42, 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task EquivalentClosedGenericCallsShareTheirReconstructedInstance()
	{
		const string source = """
			using ConstExpr;
			public static class Factory
			{
				[ConstExpr] public static T[] Create<T>(T value) => [value];
				public static bool Run() => object.ReferenceEquals(Create(42), Create<int>(value: 40 + 2));
			}
			""";

		bool result = await ConstExprFixture.CompileAndRun<bool>([new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task DifferentTypeArgumentsHaveSeparateCachedResultsAndShareBuildTimeStaticState()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Next<T>() => ++calls;
				public static ExecutionResult Run() => new([Next<int>(), Next<string>(), Next<int>()], calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 1 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task OverloadsAreSelectedBeforeSubstitutingGenericTypeArguments()
	{
		const string source = """
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Select<T>(T input) { calls++; return 1; }
				[ConstExpr] public static int Select<T>(int input) { calls++; return 2; }
				public static ExecutionResult Run() => new([Select<int>(42), Select<string>("input")], calls);
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Program.cs", source)], "Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 2, 1 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	[MethodDataSource(nameof(OpenCalls))]
	public async Task CallsWithUnresolvedTypeArgumentsWarnWithoutBuildTimeExecution(OpenCallTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			public static class Counter
			{
				[ConstExpr] public static int Calculate<T>() => throw new System.Exception("Must not execute.");
				{{testCase.Declaration}}
			}
			""";
		GeneratorRun run = GeneratorRun.Create(await CompilationFixture.Create(new SourceFile("Program.cs", source)));

		using (Assert.Multiple())
		{
			await Assert.That(run.Result.Diagnostics.Select(diagnostic => diagnostic.Id)).IsEquivalentTo(new[] { "CONSTEXPR002" }, CollectionOrdering.Matching);
			await Assert.That(run.HasInterceptor).IsFalse();
			await Assert.That(run.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
		}
	}

	public static IEnumerable<Func<GenericCallTestCase>> ClosedCalls() =>
	[
		() => new("Inferred type", "[ConstExpr] private static T Identity<T>(T input) { calls++; return input; }", "Identity(42)"),
		() => new("Explicit type", "[ConstExpr] private static T Identity<T>(T input) { calls++; return input; }", "Identity<int>(42)"),
		() => new("Constructor constraint", "[ConstExpr] private static T Create<T>() where T : new() { calls++; return new T(); }", "Create<Input>().Value"),
		() => new("Multiple type arguments", "[ConstExpr] private static T Select<T, U>(T value, U other) where T : struct { calls++; return value; }", "Select<int, string>(42, null)"),
		() => new("Collection input", "[ConstExpr] private static T First<T>(IReadOnlyList<T> items) { calls++; return items[0]; }", "First(new[] { 42 })"),
		() => new("Framework type argument", "[ConstExpr] private static int Calculate<T>() { calls++; return typeof(T) == typeof(List<int>) ? 42 : 0; }", "Calculate<List<int>>()"),
		() => new("Array type argument", "[ConstExpr] private static int Calculate<T>() { calls++; return typeof(T) == typeof(int[]) ? 42 : 0; }", "Calculate<int[]>()"),
		() => new("Dynamic type argument", "[ConstExpr] private static int Calculate<T>() { calls++; return typeof(T) == typeof(object) ? 42 : 0; }", "Calculate<dynamic>()"),
		() => new("Nullable type argument", "[ConstExpr] private static int Calculate<T>() { calls++; return typeof(T) == typeof(int?) ? 42 : 0; }", "Calculate<int?>()"),
		() => new("Private type absent from signature", "private class PrivateInput { } [ConstExpr] private static int Calculate<T>() { calls++; return typeof(T) == typeof(PrivateInput) ? 42 : 0; }", "Calculate<PrivateInput>()"),
		() => new("Closed call in generic caller", "[ConstExpr] private static T Identity<T>(T input) { calls++; return input; } private static int Read<U>() => Identity(42);", "Read<string>()"),
		() => new("Generic containing type", "private static class Factory<T> { [ConstExpr] public static T Identity(T input) { calls++; return input; } }", "Factory<int>.Identity(42)"),
		() => new("Nested generic containing types", "private static class Outer<T> { public static class Inner<U> { [ConstExpr] public static int Calculate<V>() { calls++; return typeof(T) == typeof(int) && typeof(U) == typeof(string) && typeof(V) == typeof(Input) ? 42 : 0; } } }", "Outer<int>.Inner<string>.Calculate<Input>()")
	];

	public static IEnumerable<Func<OpenCallTestCase>> OpenCalls() =>
	[
		() => new("Unused method type parameter", "public static int Read<T>() => Calculate<T>();"),
		() => new("Nested type argument", "public static int Read<T>() => Calculate<System.Collections.Generic.List<T>>();"),
		() => new("Containing type parameter", "public static class Reader<T> { public static int Read() => Calculate<T>(); }"),
		() => new("Open target containing type", "public static class Reader<T> { [ConstExpr] public static int Next() => throw new System.Exception(\"Must not execute.\"); } public static int Read<T>() => Reader<T>.Next();")
	];

	public sealed record GenericCallTestCase(string Name, string Declarations, string Call)
	{
		public override string ToString() => Name;
	}

	public sealed record OpenCallTestCase(string Name, string Declaration)
	{
		public override string ToString() => Name;
	}
}
