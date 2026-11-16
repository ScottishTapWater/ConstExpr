using ConstExpr.Roslyn.Evaluation;
using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Tests;

public sealed class ExpressionBindingComparerTests
{
	[Test]
	[MethodDataSource(nameof(ChangedBindings))]
	public async Task ChangedExpressionSemanticsAreRejected(BindingChangeTestCase testCase)
	{
		IOperation original = await CompileExpression(testCase.Declarations, testCase.OriginalExpression, "Read");
		IOperation extracted = await CompileExpression(testCase.ExtractedDeclarations ?? testCase.Declarations, testCase.ExtractedExpression, testCase.ExtractedCaller);

		await Assert.That(() => ExpressionBindingComparer.Verify(original, extracted, CancellationToken.None)).Throws<InvalidOperationException>();
	}

	[Test]
	[MethodDataSource(nameof(EquivalentBindings))]
	public async Task RelocationPreservingExpressionSemanticsIsAccepted(EquivalentBindingTestCase testCase)
	{
		IOperation original = await CompileExpression(testCase.Declarations, testCase.OriginalExpression, testCase.OriginalCaller);
		IOperation extracted = await CompileExpression(testCase.Declarations, testCase.ExtractedExpression, "Evaluate");

		ExpressionBindingComparer.Verify(original, extracted, CancellationToken.None);
	}

	private static async Task<IOperation> CompileExpression(string declarations, string expression, string caller)
	{
		string source = $$"""
			using System;
			using System.Collections.Generic;
			using System.Runtime.CompilerServices;
			public static class Counter
			{
				{{declarations}}
				private static object {{caller}}() => {{expression}};
			}
			""";
		CSharpCompilation compilation = await CompilationFixture.Create(new SourceFile("Program.cs", source));
		SyntaxTree tree = compilation.SyntaxTrees.Single();
		MethodDeclarationSyntax method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Last();
		return compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
	}

	public static IEnumerable<Func<BindingChangeTestCase>> ChangedBindings() =>
	[
		() => new("Selected overload",
			"private static int Calculate(object value, string caller = \"\") => 1; private static int Calculate(string value, string caller) => 2;",
			"Calculate(\"value\")", "Calculate(\"value\", \"\")"),
		() => new("Implicit caller member argument",
			"private static string Calculate([CallerMemberName] string caller = \"\") => caller;",
			"Calculate()", "Calculate()", ExtractedCaller: "Evaluate"),
		() => new("Implicit conversion operator",
			"public sealed class Input { public static implicit operator Box(Input value) => new(); } public sealed class Box { } private static int Calculate(Box value) => 42;",
			"Calculate(new Input())", "Calculate(new Input())",
			"public sealed class Input { } public sealed class Box { public static implicit operator Box(Input value) => new(); } private static int Calculate(Box value) => 42;"),
		() => new("Boxed constant type",
			"private static object Calculate(object value) => value;",
			"Calculate((byte)42)", "Calculate(42)"),
		() => new("Floating point negative zero",
			"private static double Calculate(double value) => value;",
			"Calculate(-0d)", "Calculate(0d)"),
		() => new("Decimal scale",
			"private static decimal Calculate(decimal value) => value;",
			"Calculate(1.0m)", "Calculate(1.00m)"),
		() => new("Implicit collection overload",
			"public sealed class Input : List<int> { public void Add(object value) => base.Add(1); public new void Add(int value) => base.Add(2); } private static int Calculate(Input input) => input[0];",
			"Calculate(new Input { (object)1 })", "Calculate(new Input { 1 })")
	];

	public static IEnumerable<Func<EquivalentBindingTestCase>> EquivalentBindings() =>
	[
		() => new("Tuple field read",
			"private static int Calculate(int value) => value;",
			"Calculate((Value: 42, Other: 1).Value)", "global::Counter.Calculate((Value: 42, Other: 1).Value)"),
		() => new("Discard argument",
			"private static int Create(out int value) { value = 0; return 42; } private static int Calculate(int value) => value;",
			"Calculate(Create(out _))", "global::Counter.Calculate(Create(out _))"),
		() => new("Anonymous property read",
			"private static int Calculate(int value) => value;",
			"Calculate(new { Value = 42 }.Value)", "global::Counter.Calculate(new { Value = 42 }.Value)"),
		() => new("Qualified static members",
			"private static int Create() => 42; private static int Calculate(int value) => value;",
			"Calculate(Create())", "global::Counter.Calculate(global::Counter.Create())"),
		() => new("Folded type parameter name",
			"private static string Calculate(string value) => value;",
			"Calculate(nameof(T))", "Calculate(\"T\")", "Read<T>"),
		() => new("Internal generic declarations",
			"private static int Calculate(int value) => value;",
			"Calculate(((Func<int>)(() => { static U Identity<U>(U value) => value; return Identity(42); }))())",
			"Calculate(((Func<int>)(() => { static U Identity<U>(U value) => value; return Identity(42); }))())")
	];

	public sealed record BindingChangeTestCase(string Name, string Declarations, string OriginalExpression, string ExtractedExpression,
		string? ExtractedDeclarations = null, string ExtractedCaller = "Read")
	{
		public override string ToString() => Name;
	}

	public sealed record EquivalentBindingTestCase(string Name, string Declarations, string OriginalExpression, string ExtractedExpression, string OriginalCaller = "Read")
	{
		public override string ToString() => Name;
	}
}
