using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class DirectCallTests
{
	[Test]
	[MethodDataSource(nameof(DirectCallCases))]
	public async Task DirectCallsToMarkedMethodsReturnPrecomputedResults(DirectCallTestCase testCase)
	{
		CSharpCompilation input = await CompilationFixture.Create(testCase.Sources);
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, testCase.EntryType);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	public static IEnumerable<Func<DirectCallTestCase>> DirectCallCases()
	{
		yield return () => new("Unqualified", [new("Program.cs", FixtureSources.Simple)], "Fixture.Counter");
		yield return () => new("Type qualified", [new("Program.cs", Qualify("Counter.CountUp"))], "Fixture.Counter");
		yield return () => new("Namespace qualified", [new("Program.cs", Qualify("Fixture.Counter.CountUp"))], "Fixture.Counter");
		yield return () => new("Globally qualified", [new("Program.cs", Qualify("global::Fixture.Counter.CountUp"))], "Fixture.Counter");
		yield return () => new("Type alias", [new("Program.cs", "using Target = Fixture.Counter;\n" + Qualify("Target.CountUp"))], "Fixture.Counter");
		yield return () => new("Using static",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller("CountUp()", imports: "using static Fixture.Counter;"))],
			"Fixture.Caller");
		yield return () => new("Other class in the same file",
			[new("Program.cs", FixtureSources.Counter + "\n" + FixtureSources.Caller()[FixtureSources.Caller().IndexOf("public static class Caller", StringComparison.Ordinal)..])],
			"Fixture.Caller");
		yield return () => new("Other source file",
			[new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller())],
			"Fixture.Caller");
	}

	private static string Qualify(string method) => FixtureSources.Simple.Replace("= CountUp();", $"= {method}();", StringComparison.Ordinal);
}
