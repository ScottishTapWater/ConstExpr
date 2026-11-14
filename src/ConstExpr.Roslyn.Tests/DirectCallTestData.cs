using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public static class DirectCallTestData
{
	public static IEnumerable<Func<(string Name, (string Path, string Source)[] Sources, string EntryType)>> Cases()
	{
		yield return () => ("Unqualified", [("Program.cs", FixtureSources.Simple)], "Fixture.Counter");
		yield return () => ("Type qualified", [("Program.cs", Qualify("Counter.CountUp"))], "Fixture.Counter");
		yield return () => ("Namespace qualified", [("Program.cs", Qualify("Fixture.Counter.CountUp"))], "Fixture.Counter");
		yield return () => ("Globally qualified", [("Program.cs", Qualify("global::Fixture.Counter.CountUp"))], "Fixture.Counter");
		yield return () => ("Type alias", [("Program.cs", "using Target = Fixture.Counter;\n" + Qualify("Target.CountUp"))], "Fixture.Counter");
		yield return () => ("Using static",
			[("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller("CountUp()", imports: "using static Fixture.Counter;"))],
			"Fixture.Caller");
		yield return () => ("Other class in the same file",
			[("Program.cs", FixtureSources.Counter + "\n" + FixtureSources.Caller()[FixtureSources.Caller().IndexOf("public static class Caller", StringComparison.Ordinal)..])],
			"Fixture.Caller");
		yield return () => ("Other source file",
			[("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller())],
			"Fixture.Caller");
	}

	private static string Qualify(string method) => FixtureSources.Simple.Replace("= CountUp();", $"= {method}();", StringComparison.Ordinal);
}
