using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed record DirectCallTestCase(string Name, SourceFile[] Sources, string EntryType)
{
	public override string ToString() => Name;
}
