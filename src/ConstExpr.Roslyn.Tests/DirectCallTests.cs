using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class DirectCallTests
{
	[Test]
	[Arguments(DirectCallForm.Unqualified)]
	[Arguments(DirectCallForm.TypeQualified)]
	[Arguments(DirectCallForm.NamespaceQualified)]
	[Arguments(DirectCallForm.GloballyQualified)]
	[Arguments(DirectCallForm.TypeAlias)]
	[Arguments(DirectCallForm.UsingStatic)]
	[Arguments(DirectCallForm.OtherClassSameFile)]
	[Arguments(DirectCallForm.OtherSourceFile)]
	public async Task DirectCallUsesTheBakedResult(DirectCallForm form)
	{
		(string Path, string Source)[] sources = Sources(form);
		CSharpCompilation input = await CompilationFixture.Create(sources);
		string entryType = form is DirectCallForm.UsingStatic or DirectCallForm.OtherClassSameFile or DirectCallForm.OtherSourceFile
			? "Fixture.Caller" : "Fixture.Counter";
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, entryType);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	private static (string Path, string Source)[] Sources(DirectCallForm form) => form switch
	{
		DirectCallForm.Unqualified => [("Program.cs", FixtureSources.Simple)],
		DirectCallForm.TypeQualified => [("Program.cs", Qualify("Counter.CountUp"))],
		DirectCallForm.NamespaceQualified => [("Program.cs", Qualify("Fixture.Counter.CountUp"))],
		DirectCallForm.GloballyQualified => [("Program.cs", Qualify("global::Fixture.Counter.CountUp"))],
		DirectCallForm.TypeAlias => [("Program.cs", "using Target = Fixture.Counter;\n" + Qualify("Target.CountUp"))],
		DirectCallForm.UsingStatic => [("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller("CountUp()", imports: "using static Fixture.Counter;"))],
		DirectCallForm.OtherClassSameFile => [("Program.cs", FixtureSources.Counter + "\n" + FixtureSources.Caller()[FixtureSources.Caller().IndexOf("public static class Caller", StringComparison.Ordinal)..])],
		DirectCallForm.OtherSourceFile => [("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller())],
		_ => throw new ArgumentOutOfRangeException(nameof(form))
	};

	private static string Qualify(string method) => FixtureSources.Simple.Replace("= CountUp();", $"= {method}();", StringComparison.Ordinal);
}
