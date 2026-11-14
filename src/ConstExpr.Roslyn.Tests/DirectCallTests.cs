using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class DirectCallTests
{
	[Test]
	[MethodDataSource(typeof(DirectCallTestData), nameof(DirectCallTestData.Cases))]
	public async Task DirectCallsToMarkedMethodsReturnPrecomputedResults(string name, (string Path, string Source)[] sources, string entryType)
	{
		CSharpCompilation input = await CompilationFixture.Create(sources);
		using LoadedFixture fixture = new(GeneratorRun.Create(input).Compilation, entryType);

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}
}
