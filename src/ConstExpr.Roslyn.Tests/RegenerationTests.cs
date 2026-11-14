using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class RegenerationTests
{
	[Test]
	public async Task InsertingALineBeforeTheCallUpdatesItsLocation()
	{
		CSharpCompilation input = await Input();
		GeneratorRun initial = GeneratorRun.Create(input);
		CSharpCompilation edited = ReplaceCaller(input, "\n" + FixtureSources.Caller());

		GeneratorRun updated = GeneratorRun.Create(edited, initial.Driver);
		using LoadedFixture fixture = new(updated.Compilation, "Fixture.Caller");
		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task EditingAfterTheCallUpdatesTheFileChecksum()
	{
		CSharpCompilation input = await Input();
		GeneratorRun initial = GeneratorRun.Create(input);
		CSharpCompilation edited = ReplaceCaller(input, FixtureSources.Caller() + "\n// An edit after the call.\n");

		GeneratorRun updated = GeneratorRun.Create(edited, initial.Driver);
		using LoadedFixture fixture = new(updated.Compilation, "Fixture.Caller");
		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task MovingTheCallToAnotherFileStillInterceptsIt()
	{
		CSharpCompilation input = await Input();
		GeneratorRun initial = GeneratorRun.Create(input);
		SyntaxTree original = input.SyntaxTrees.Single(tree => tree.FilePath == "Caller.cs");
		CSharpCompilation edited = input.RemoveSyntaxTrees(original)
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(FixtureSources.Caller(), CompilationFixture.ParseOptions, "MovedCaller.cs"));

		GeneratorRun updated = GeneratorRun.Create(edited, initial.Driver);
		using LoadedFixture fixture = new(updated.Compilation, "Fixture.Caller");
		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { 1, 1, 1 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task AddingASecondCallSiteInterceptsBothCalls()
	{
		CSharpCompilation input = await Input();
		GeneratorRun initial = GeneratorRun.Create(input);
		CSharpCompilation edited = ReplaceCaller(input, FixtureSources.Caller("Counter.CountUp() + Counter.CountUp()"));

		GeneratorRun updated = GeneratorRun.Create(edited, initial.Driver);
		using LoadedFixture fixture = new(updated.Compilation, "Fixture.Caller");
		int[] values = await fixture.Run();

		await Assert.That(values).IsEquivalentTo(new[] { values[0], values[0], values[0] });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	[Test]
	public async Task RemovingTheCallSiteRemovesItsInterceptor()
	{
		CSharpCompilation input = await Input();
		GeneratorRun initial = GeneratorRun.Create(input);
		CSharpCompilation edited = ReplaceCaller(input, FixtureSources.Caller("42"));

		GeneratorRun updated = GeneratorRun.Create(edited, initial.Driver);
		using LoadedFixture fixture = new(updated.Compilation, "Fixture.Caller");
		int[] values = await fixture.Run();

		await Assert.That(initial.HasInterceptor).IsTrue();
		await Assert.That(updated.HasInterceptor).IsFalse();
		await Assert.That(values).IsEquivalentTo(new[] { 42, 42, 42 });
		await Assert.That(fixture.Calls).IsEqualTo(0);
	}

	private static Task<CSharpCompilation> Input() => CompilationFixture.Create(
		("Counter.cs", FixtureSources.Counter), ("Caller.cs", FixtureSources.Caller()));

	private static CSharpCompilation ReplaceCaller(CSharpCompilation input, string source)
	{
		SyntaxTree original = input.SyntaxTrees.Single(tree => tree.FilePath == "Caller.cs");
		return input.ReplaceSyntaxTree(original, CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "Caller.cs"));
	}
}
