using System.Runtime.Loader;
using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class ExternAliasInputTests : IDisposable
{
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("constexpr-alias-");

	[Test]
	[MethodDataSource(nameof(AliasImports))]
	public async Task GenericInputsPreserveExternalAliasResolution(AliasImportTestCase testCase)
	{
		const string dependencySource = """
			namespace Dependency;
			public static class Input
			{
				public static int Create() => 42;
			}
			""";
		string dependencyName = "Dependency_" + Guid.NewGuid().ToString("N");
		CSharpCompilation dependency = (await CompilationFixture.Create(new SourceFile("Dependency.cs", dependencySource))).WithAssemblyName(dependencyName);
		using MemoryStream dependencyImage = new();
		CompilationFixture.ThrowOnErrors(dependency.Emit(dependencyImage).Diagnostics);
		string dependencyPath = Path.Combine(_directory.FullName, dependencyName + ".dll");
		byte[] dependencyBytes = dependencyImage.ToArray();
		await File.WriteAllBytesAsync(dependencyPath, dependencyBytes);
		PortableExecutableReference reference = MetadataReference.CreateFromImage(dependencyBytes,
			new MetadataReferenceProperties(aliases: ["dep"]), filePath: dependencyPath);
		string source = $$"""
			extern alias dep;
			{{testCase.Import}}
			using ConstExpr;
			using ConstExpr.Roslyn.Tests.Infrastructure;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int input)
				{
					if (input != 42) { throw new System.InvalidOperationException("Unexpected input."); }
					calls++;
					return input;
				}
				private static int Read<T>() => Calculate(Input.Create());
				public static ExecutionResult Run() => new(new[] { Read<int>() }, calls);
			}
			""";
		CSharpCompilation input = (await CompilationFixture.Create()).AddReferences(reference)
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "Program.cs"));
		CompilationFixture.ThrowOnErrors(input.GetDiagnostics());

		GeneratorRun run = GeneratorRun.Create(input);
		dependencyImage.Position = 0;
		AssemblyLoadContext.Default.LoadFromStream(dependencyImage);
		using LoadedFixture fixture = new(run.Compilation);
		ExecutionResult result = await fixture.Run<ExecutionResult>("Counter.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 42 }, CollectionOrdering.Matching);
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	public static IEnumerable<Func<AliasImportTestCase>> AliasImports() =>
	[
		() => new("Type alias", "using Input = dep::Dependency.Input;"),
		() => new("Namespace import", "using dep::Dependency;")
	];

	public void Dispose() => _directory.Delete(recursive: true);

	public sealed record AliasImportTestCase(string Name, string Import)
	{
		public override string ToString() => Name;
	}
}
