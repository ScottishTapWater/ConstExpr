using System.Runtime.Loader;
using ConstExpr.Roslyn.Generators;
using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class EvaluationDependencyTests : IDisposable
{
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("constexpr-dependencies-");

	[Test]
	public async Task NonmanagedRuntimeAssetsDoNotPreventEvaluation()
	{
		string nativePath = Path.Combine(_directory.FullName, "NativeAsset.dll");
		await File.WriteAllTextAsync(nativePath, "This runtime asset is not a managed assembly.");
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate() => ++calls;
				public static int[] Run() => [Calculate(), calls];
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new ConstExprGenerator().AsSourceGenerator()],
			parseOptions: CompilationFixture.ParseOptions, optionsProvider: new RuntimeAssemblyOptionsProvider(nativePath));

		driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics);
		CompilationFixture.ThrowOnErrors(diagnostics);
		using LoadedFixture fixture = new(output);
		int[] result = await fixture.Run<int[]>("Counter.Run");

		await Assert.That(result).IsEquivalentTo(new[] { 1, 0 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task UnusedX86ReferencesDoNotPreventEvaluation()
	{
		string dependencyName = "UnusedX86_" + Guid.NewGuid().ToString("N");
		CSharpCompilation dependency = (await CompilationFixture.CreateWithOptions(
			[new("Unused.cs", "public sealed class Unused { }")], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: Platform.X86)))
			.WithAssemblyName(dependencyName);
		string dependencyPath = Path.Combine(_directory.FullName, dependencyName + ".dll");
		using (FileStream stream = File.Create(dependencyPath))
		{
			CompilationFixture.ThrowOnErrors(dependency.Emit(stream).Diagnostics);
		}
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate() => ++calls;
				public static int[] Run() => [Calculate(), calls];
			}
			""";
		CSharpCompilation input = (await CompilationFixture.Create(new SourceFile("Program.cs", source)))
			.AddReferences(MetadataReference.CreateFromFile(dependencyPath));

		GeneratorRun run = GeneratorRun.Create(input);
		using LoadedFixture fixture = new(run.Compilation);
		int[] result = await fixture.Run<int[]>("Counter.Run");

		await Assert.That(result).IsEquivalentTo(new[] { 1, 0 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task CompatibleRuntimeVersionsAreEvaluatedPrivatelyWhenAlreadyLoadedByTheHost()
	{
		string dependencyName = "VersionedDependency_" + Guid.NewGuid().ToString("N");
		string referencePath = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory.FullName, "reference")).FullName, dependencyName + ".dll");
		string runtimePath = Path.Combine(Directory.CreateDirectory(Path.Combine(_directory.FullName, "runtime")).FullName, dependencyName + ".dll");
		const string referenceSource = """
			[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
			namespace Dependency;
			public static class Values
			{
				public static int Calls { get; private set; }
				public static int Next() => ++Calls;
			}
			""";
		const string runtimeSource = """
			[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
			namespace Dependency;
			public static class Values
			{
				public static int Calls { get; private set; }
				public static int Next() => 41 + ++Calls;
			}
			""";
		CSharpCompilation reference = (await CompilationFixture.Create(new SourceFile("Dependency.cs", referenceSource))).WithAssemblyName(dependencyName);
		CSharpCompilation runtime = (await CompilationFixture.Create(new SourceFile("Dependency.cs", runtimeSource))).WithAssemblyName(dependencyName);
		using (FileStream stream = File.Create(referencePath))
		{
			CompilationFixture.ThrowOnErrors(reference.Emit(stream).Diagnostics);
		}
		using MemoryStream runtimeImage = new();
		CompilationFixture.ThrowOnErrors(runtime.Emit(runtimeImage).Diagnostics);
		await File.WriteAllBytesAsync(runtimePath, runtimeImage.ToArray());
		runtimeImage.Position = 0;
		AssemblyLoadContext.Default.LoadFromStream(runtimeImage);
		const string source = """
			using ConstExpr;
			public static class Factory
			{
				[ConstExpr] public static int Create() => Dependency.Values.Next();
				public static int[] Run() => [Create(), Dependency.Values.Calls];
			}
			""";
		CSharpCompilation input = (await CompilationFixture.Create())
			.AddReferences(MetadataReference.CreateFromFile(referencePath))
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "Program.cs"));
		CompilationFixture.ThrowOnErrors(input.GetDiagnostics());
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new ConstExprGenerator().AsSourceGenerator()],
			parseOptions: CompilationFixture.ParseOptions, optionsProvider: new RuntimeAssemblyOptionsProvider(runtimePath));

		driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics);
		CompilationFixture.ThrowOnErrors(diagnostics);
		using LoadedFixture fixture = new(output);
		int[] result = await fixture.Run<int[]>("Factory.Run");

		await Assert.That(result).IsEquivalentTo(new[] { 42, 0 }, CollectionOrdering.Matching);
	}

	public void Dispose() => _directory.Delete(recursive: true);

	private sealed class RuntimeAssemblyOptionsProvider : AnalyzerConfigOptionsProvider
	{
		private readonly RuntimeAssemblyOptions _global;
		private readonly RuntimeAssemblyOptions _empty = new("");

		internal RuntimeAssemblyOptionsProvider(string path)
		{
			_global = new(path);
		}

		public override AnalyzerConfigOptions GlobalOptions => _global;
		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;
		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
	}

	private sealed class RuntimeAssemblyOptions : AnalyzerConfigOptions
	{
		private readonly string _path;

		internal RuntimeAssemblyOptions(string path)
		{
			_path = path;
		}

		public override bool TryGetValue(string key, out string value)
		{
			value = key == "build_property.ConstExprRuntimeAssemblies" ? _path : "";
			return value.Length > 0;
		}
	}
}
