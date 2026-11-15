using System.Collections.Immutable;
using ConstExpr.Roslyn.Analysers;
using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ConstExpr.Roslyn.Tests;

public sealed class IndirectCallWarningTests
{
	[Test]
	[MethodDataSource(nameof(MethodGroupConversions))]
	public async Task ConvertingAMarkedMethodGroupToADelegateReportsAWarningAtTheReference(string statement)
	{
		await AssertWarning(statement);
	}

	[Test]
	public async Task TakingAMarkedMethodsAddressReportsAWarningAtTheReference()
	{
		await AssertWarning("delegate*<int> next = &Counter.Next;");
	}

	[Test]
	[MethodDataSource(nameof(MethodGroupConversions))]
	public async Task ConvertingAnUnmarkedMethodGroupToADelegateDoesNotWarn(string statement)
	{
		ImmutableArray<Diagnostic> diagnostics = await Analyse(statement, "public static int Next() => 1;");

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task TakingAnUnmarkedMethodsAddressDoesNotWarn()
	{
		ImmutableArray<Diagnostic> diagnostics = await Analyse("delegate*<int> next = &Counter.Next;", "public static int Next() => 1;");

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	[MethodDataSource(nameof(OrdinaryMethodUsages))]
	public async Task UsingAMarkedMethodWithoutAnIndirectReferenceDoesNotWarn(string statement)
	{
		ImmutableArray<Diagnostic> diagnostics = await Analyse(statement, "[ConstExpr] public static int Next() => 1;");

		await Assert.That(diagnostics).IsEmpty();
	}

	public static IEnumerable<string> MethodGroupConversions() =>
	[
		"Func<int> next = Counter.Next;",
		"var next = Counter.Next;",
		"Func<int> next = new Func<int>(Counter.Next);",
		"Func<int> next = (Func<int>)Counter.Next;",
		"Accept(Counter.Next);"
	];

	public static IEnumerable<string> OrdinaryMethodUsages() =>
	[
		"int value = Counter.Next();",
		"Func<int> next = () => Counter.Next();",
		"string name = nameof(Counter.Next);"
	];

	private static async Task AssertWarning(string statement)
	{
		ImmutableArray<Diagnostic> diagnostics = await Analyse(statement, "[ConstExpr] public static int Next() => 1;");

		await Assert.That(diagnostics).Count().IsEqualTo(1);
		Diagnostic diagnostic = diagnostics.Single();
		using (Assert.Multiple())
		{
			await Assert.That(diagnostic.Id).IsEqualTo("CONSTEXPR003");
			await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
			await Assert.That(diagnostic.GetMessage()).IsEqualTo(
				"Reference to 'Counter.Next()' bypasses compile-time evaluation; invoking the delegate or function pointer will execute the original method at runtime");
			await Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo("Counter.Next");
		}
	}

	private static async Task<ImmutableArray<Diagnostic>> Analyse(string statement, string method)
	{
		string source = $$"""
			using System;
			using ConstExpr;

			public static class Counter
			{
				{{method}}
				private static void Accept(Func<int> next) { }

				public static unsafe void Run()
				{
					{{statement}}
				}
			}
			""";
		CSharpCompilation compilation = await CompilationFixture.CreateWithOptions(
			[new("Program.cs", source)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
		return await compilation.WithAnalyzers([new IndirectCallAnalyser()]).GetAnalyzerDiagnosticsAsync();
	}
}
