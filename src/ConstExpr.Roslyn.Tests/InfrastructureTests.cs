using ConstExpr.Roslyn.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Tests;

public sealed class InfrastructureTests
{
	[Test]
	public async Task RecognisesInterceptionBoundByTheCompiler()
	{
		CSharpCompilation input = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));

		GeneratorRun run = GeneratorRun.Create(ManualInterceptor.Add(input));

		await Assert.That(run.HasInterceptor).IsTrue();
	}

	[Test]
	public async Task GeneratedLookalikeAttributeDoesNotCountAsInterception()
	{
		CSharpCompilation input = await CompilationFixture.Create(new SourceFile("Program.cs", FixtureSources.Simple));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new LookalikeAttributeGenerator().AsSourceGenerator()], parseOptions: CompilationFixture.ParseOptions);

		GeneratorRun run = GeneratorRun.Create(input, driver);

		await Assert.That(run.HasInterceptor).IsFalse();
	}

	[Test]
	public async Task CompileAndRunReturnsObservationsFromTheSpecifiedAsyncMethod()
	{
		const string source = """
			using System.Threading.Tasks;
			using ConstExpr.Roslyn.Tests.Infrastructure;

			namespace Sample;

			public static class Observations
			{
				public static async Task<ExecutionResult> Capture()
				{
					await Task.Yield();
					int calls = 0;
					int[] values = new int[3];
					for (int index = 0; index < values.Length; index++)
					{
						values[index] = ++calls;
					}
					return new ExecutionResult(values, calls);
				}
			}
			""";

		ExecutionResult result = await ConstExprFixture.CompileAndRun([new("Observations.cs", source)], "Sample.Observations.Capture");

		await Assert.That(result).IsEquivalentTo(new ExecutionResult([1, 2, 3], 3));
	}

	[Test]
	public async Task CallsWithoutInterceptionExecuteTheOriginalMethodEachTime()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture fixture = new(compilation);

		ExecutionResult result = await fixture.Run<ExecutionResult>("Fixture.Caller.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}

	[Test]
	public async Task ManualInterceptionReturnsTheReplacementResultWithoutExecutingTheOriginalMethod()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture fixture = new(ManualInterceptor.Add(compilation));

		ExecutionResult result = await fixture.Run<ExecutionResult>("Fixture.Caller.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 7, 7, 7 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(0);
		}
	}

	[Test]
	public async Task SeparateAssemblyLoadsDoNotShareStaticState()
	{
		CSharpCompilation compilation = await CompilationFixture.Create(new("Counter.cs", FixtureSources.Counter), new("Caller.cs", FixtureSources.Caller()));
		using LoadedFixture first = new(compilation);
		await first.Run<ExecutionResult>("Fixture.Caller.Run");
		using LoadedFixture second = new(compilation);

		ExecutionResult result = await second.Run<ExecutionResult>("Fixture.Caller.Run");

		using (Assert.Multiple())
		{
			await Assert.That(result.Values).IsEquivalentTo(new[] { 1, 2, 3 });
			await Assert.That(result.RuntimeCalls).IsEqualTo(3);
		}
	}

	private sealed class LookalikeAttributeGenerator : IIncrementalGenerator
	{
		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput(output => output.AddSource("Lookalike.g.cs", """
				namespace Lookalike;

				[System.AttributeUsage(System.AttributeTargets.Method)]
				internal sealed class InterceptsLocationAttribute : System.Attribute
				{
				}

				internal static class Replacement
				{
					[InterceptsLocation]
					internal static int Run() => 7;
				}
				"""));
		}
	}
}
