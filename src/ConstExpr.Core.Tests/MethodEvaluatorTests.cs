using System.Reflection;
using System.Runtime.Loader;
using ConstExpr.Core.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Core.Tests;

public sealed class MethodEvaluatorTests
{
	[Test]
	public async Task EvaluationExecutesTheSuppliedMethodAndReturnsItsResult()
	{
		const string source = """
			public static class Counter
			{
				private static int counter;
				public static int CountUp() => ++counter;
			}
			""";
		CSharpCompilation compilation = CSharpCompilation.Create(
			$"Evaluation_{Guid.NewGuid():N}",
			[CSharpSyntaxTree.ParseText(source)],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using MemoryStream stream = new();
		Microsoft.CodeAnalysis.Emit.EmitResult emitted = compilation.Emit(stream);
		if (!emitted.Success)
		{
			throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
		}
		stream.Position = 0;
		AssemblyLoadContext context = new(compilation.AssemblyName, isCollectible: true);
		try
		{
			Assembly assembly = context.LoadFromStream(stream);
			MethodInfo method = assembly.GetType("Counter", throwOnError: true)!.GetMethod("CountUp")!;
			MethodEvaluator evaluator = new();

			object? result = evaluator.Evaluate(method);

			await Assert.That(result).IsEqualTo(1);
		}
		finally
		{
			context.Unload();
		}
	}
}
