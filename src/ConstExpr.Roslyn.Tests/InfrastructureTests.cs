using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class InfrastructureTests
{
	[Test]
	public async Task CallsWithoutInterceptionExecuteTheOriginalMethodEachTime()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture fixture = new(compilation, "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(fixture.Calls).IsEqualTo(3);
	}

	[Test]
	public async Task ManualInterceptionReturnsTheReplacementResultWithoutExecutingTheOriginalMethod()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture fixture = new(ManualInterceptor.Add(compilation), "Fixture.Caller");

		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 7, 7, 7 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task SeparateAssemblyLoadsDoNotShareStaticState()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture first = new(compilation, "Fixture.Caller");
		await first.Run();
		using LoadedFixture second = new(compilation, "Fixture.Caller");

		int[] values = await second.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 2, 3 });
		await Assert.That(second.Calls).IsEqualTo(3);
	}
}
