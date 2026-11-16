using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using ConstExpr.Roslyn.Generators;
using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TUnit.Assertions.Enums;

namespace ConstExpr.Roslyn.Tests;

public sealed class EvaluationLifetimeTests
{
	[Test]
	public async Task ConstantEvaluationsShareBuildTimeStateAndReuseCachedResults()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int value) => ++calls;
				public static int[] Run() => [Calculate(10), Calculate(20), Calculate(10)];
			}
			""";

		int[] result = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEquivalentTo(new[] { 1, 2, 1 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task SeparateExpressionCallSitesHaveIndependentBuildTimeState()
	{
		const string source = """
			using ConstExpr;
			public static class Counter
			{
				private static int calls;
				[ConstExpr] public static int Calculate(int value) => ++calls;
				public static int[] Run() => [Calculate(System.Math.Abs(-10)), Calculate(System.Math.Abs(-20))];
			}
			""";

		int[] result = await ConstExprFixture.CompileAndRun<int[]>([new("Program.cs", source)], "Counter.Run");

		await Assert.That(result).IsEquivalentTo(new[] { 1, 1 }, CollectionOrdering.Matching);
	}

	[Test]
	[MethodDataSource(nameof(EvaluatedCalls))]
	public async Task RepeatedGeneratorRunsReleaseEvaluationAssemblies(EvaluatedCallTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			public sealed class Result { public int Value { get; set; } }
			public static class Counter
			{
				[ConstExpr] public static Result Calculate(int value) => new() { Value = value };
				public static Result Run() => Calculate({{testCase.Argument}});
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		EvaluationObservation observation = RunAndObserveAssemblies(input, 3);
		Collect(observation);

		using (Assert.Multiple())
		{
			await Assert.That(observation.Assemblies).Count().IsEqualTo(3);
			await Assert.That(observation.Assemblies.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.SupportingAssemblies).IsNotEmpty();
			await Assert.That(observation.SupportingAssemblies.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.Contexts.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
		}
		GC.KeepAlive(observation.Driver);
	}

	[Test]
	public async Task RepeatedGeneratorRunsReleaseReferencedDependencies()
	{
		DirectoryInfo directory = Directory.CreateTempSubdirectory("constexpr-lifetime-");
		try
		{
			const string dependencySource = """
				public static class Input { public static int Create() => 42; }
				""";
			string dependencyName = "Dependency_" + Guid.NewGuid().ToString("N");
			CSharpCompilation dependency = (await CompilationFixture.Create(new SourceFile("Dependency.cs", dependencySource)))
				.WithAssemblyName(dependencyName);
			string dependencyPath = Path.Combine(directory.FullName, dependencyName + ".dll");
			using (FileStream stream = File.Create(dependencyPath))
			{
				CompilationFixture.ThrowOnErrors(dependency.Emit(stream).Diagnostics);
			}
			const string source = """
				using ConstExpr;
				public static class Counter
				{
					[ConstExpr] public static int Calculate(int value) => value;
					public static int Run() => Calculate(Input.Create());
				}
				""";
			CSharpCompilation input = (await CompilationFixture.Create())
				.AddReferences(MetadataReference.CreateFromFile(dependencyPath))
				.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "Program.cs"));
			CompilationFixture.ThrowOnErrors(input.GetDiagnostics());

			EvaluationObservation observation = RunAndObserveAssemblies(input, 3, dependencyName);
			Collect(observation);

			using (Assert.Multiple())
			{
				await Assert.That(observation.Assemblies).Count().IsEqualTo(6);
				await Assert.That(observation.Assemblies.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.SupportingAssemblies).IsNotEmpty();
				await Assert.That(observation.SupportingAssemblies.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.Contexts.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
			}
			GC.KeepAlive(observation.Driver);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Test]
	public async Task AlreadyLoadedDependencyConvertersRunInsideEvaluationAndReconstructTheirResults()
	{
		DirectoryInfo directory = Directory.CreateTempSubdirectory("constexpr-converter-");
		try
		{
			ReferencedCompilation input = await CreateInputWithAlreadyLoadedConverter(directory);

			GeneratorRun run = GeneratorRun.Create(input.Compilation);
			using LoadedFixture fixture = new(run.Compilation);
			int[] result = await fixture.Run<int[]>("Factory.Run");

			await Assert.That(result).IsEquivalentTo(new[] { 42, 0, 1, 0 }, CollectionOrdering.Matching);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Test]
	public async Task EvaluationReleasesPrivateCopiesOfAlreadyLoadedConverterDependencies()
	{
		DirectoryInfo directory = Directory.CreateTempSubdirectory("constexpr-converter-lifetime-");
		try
		{
			ReferencedCompilation input = await CreateInputWithAlreadyLoadedConverter(directory);

			EvaluationObservation observation = RunAndObserveAssemblies(input.Compilation, 1, input.DependencyName);
			Collect(observation);

			using (Assert.Multiple())
			{
				await Assert.That(observation.Assemblies).Count().IsEqualTo(2);
				await Assert.That(observation.Assemblies.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.SupportingAssemblies).IsNotEmpty();
				await Assert.That(observation.SupportingAssemblies.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.Contexts.Any(reference => reference.IsAlive)).IsFalse();
				await Assert.That(observation.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsEmpty();
			}
			GC.KeepAlive(observation.Driver);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Test]
	[MethodDataSource(nameof(EvaluationFailures))]
	public async Task FailedEvaluationReleasesItsAssemblies(EvaluationFailureTestCase testCase)
	{
		string source = $$"""
			using ConstExpr;
			public sealed class Result { public int Value => throw new System.InvalidOperationException("Serialization failed."); }
			public static class Counter
			{
				[ConstExpr] public static Result Calculate() => {{testCase.Body}};
				public static Result Run() => Calculate();
			}
			""";
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", source));

		EvaluationObservation observation = RunAndObserveAssemblies(input, 1);
		Collect(observation);

		using (Assert.Multiple())
		{
			await Assert.That(observation.Assemblies).Count().IsEqualTo(1);
			await Assert.That(observation.Assemblies.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.SupportingAssemblies).IsNotEmpty();
			await Assert.That(observation.SupportingAssemblies.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.Contexts.Any(reference => reference.IsAlive)).IsFalse();
			await Assert.That(observation.Diagnostics.Select(diagnostic => diagnostic.Id))
				.IsEquivalentTo(new[] { "CONSTEXPR001" }, CollectionOrdering.Matching);
		}
		GC.KeepAlive(observation.Driver);
	}

	public static IEnumerable<Func<EvaluatedCallTestCase>> EvaluatedCalls() =>
	[
		() => new("Constant argument", "42"),
		() => new("Expression argument", "System.Math.Abs(-42)")
	];

	public static IEnumerable<Func<EvaluationFailureTestCase>> EvaluationFailures() =>
	[
		() => new("Invocation failure", "throw new System.InvalidOperationException(\"Evaluation failed.\")"),
		() => new("Serialization failure", "new Result()")
	];

	private static async Task<ReferencedCompilation> CreateInputWithAlreadyLoadedConverter(DirectoryInfo directory)
	{
		const string dependencySource = """
			using System;
			using System.Text.Json;
			using System.Text.Json.Serialization;
			namespace Dependency;
			[JsonConverter(typeof(SecretConverter))]
			public sealed class Secret
			{
				private readonly int value;
				public Secret(int value) { this.value = value; }
				public int ReadValue() => value;
			}
			public sealed class SecretConverter : JsonConverter<Secret>
			{
				public static int Reads;
				public static int Writes;
				public override Secret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
				{
					Reads++;
					return new(reader.GetInt32());
				}
				public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options)
				{
					Writes++;
					writer.WriteNumberValue(value.ReadValue());
				}
			}
			""";
		string dependencyName = "ConverterDependency_" + Guid.NewGuid().ToString("N");
		CSharpCompilation dependency = (await CompilationFixture.Create(new SourceFile("Dependency.cs", dependencySource)))
			.WithAssemblyName(dependencyName);
		using MemoryStream dependencyImage = new();
		CompilationFixture.ThrowOnErrors(dependency.Emit(dependencyImage).Diagnostics);
		byte[] bytes = dependencyImage.ToArray();
		string dependencyPath = Path.Combine(directory.FullName, dependencyName + ".dll");
		await File.WriteAllBytesAsync(dependencyPath, bytes);
		dependencyImage.Position = 0;
		AssemblyLoadContext.Default.LoadFromStream(dependencyImage);
		const string source = """
			using ConstExpr;
			using Dependency;
			public static class Factory
			{
				private static int calls;
				[ConstExpr] public static Secret Create() { calls++; return new(42); }
				public static int[] Run() => [Create().ReadValue(), calls, SecretConverter.Reads, SecretConverter.Writes];
			}
			""";
		CSharpCompilation input = (await CompilationFixture.Create())
			.AddReferences(MetadataReference.CreateFromImage(bytes, filePath: dependencyPath))
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "Program.cs"));
		CompilationFixture.ThrowOnErrors(input.GetDiagnostics());
		return new(input, dependencyName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static EvaluationObservation RunAndObserveAssemblies(CSharpCompilation input, int repetitions, string? dependencyName = null)
	{
		List<WeakReference> assemblies = [];
		List<WeakReference> supportingAssemblies = [];
		List<WeakReference> contexts = [];
		List<Diagnostic> diagnostics = [];
		string prefix = input.AssemblyName + ".";
		void Observe(object? sender, AssemblyLoadEventArgs args)
		{
			string? name = args.LoadedAssembly.GetName().Name;
			AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly);
			if (name is not null && (name.StartsWith(prefix, StringComparison.Ordinal) || name == dependencyName))
			{
				assemblies.Add(new WeakReference(args.LoadedAssembly));
				contexts.Add(new WeakReference(context));
			}
			else if (context?.Name?.Contains(prefix, StringComparison.Ordinal) == true)
			{
				supportingAssemblies.Add(new WeakReference(args.LoadedAssembly));
				contexts.Add(new WeakReference(context));
			}
		}
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new ConstExprGenerator().AsSourceGenerator()], parseOptions: CompilationFixture.ParseOptions);
		AppDomain.CurrentDomain.AssemblyLoad += Observe;
		try
		{
			for (int iteration = 0; iteration < repetitions; iteration++)
			{
				driver = driver.RunGenerators(input);
				diagnostics.AddRange(driver.GetRunResult().Diagnostics);
				SyntaxTree tree = input.SyntaxTrees.Single();
				input = input.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText("\n" + tree.GetText(), CompilationFixture.ParseOptions, tree.FilePath));
			}
		}
		finally
		{
			AppDomain.CurrentDomain.AssemblyLoad -= Observe;
		}
		return new(assemblies.ToArray(), supportingAssemblies.ToArray(), contexts.ToArray(), diagnostics.ToArray(), driver);
	}

	private static void Collect(EvaluationObservation observation)
	{
		WeakReference[] assemblies = observation.Assemblies.Concat(observation.SupportingAssemblies).Concat(observation.Contexts).ToArray();
		for (int attempt = 0; attempt < 10 && assemblies.Any(reference => reference.IsAlive); attempt++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
	}

	public sealed record EvaluatedCallTestCase(string Name, string Argument)
	{
		public override string ToString() => Name;
	}

	public sealed record EvaluationFailureTestCase(string Name, string Body)
	{
		public override string ToString() => Name;
	}

	private sealed record EvaluationObservation(WeakReference[] Assemblies, WeakReference[] SupportingAssemblies, WeakReference[] Contexts, Diagnostic[] Diagnostics, GeneratorDriver Driver);

	private sealed record ReferencedCompilation(CSharpCompilation Compilation, string DependencyName);
}
