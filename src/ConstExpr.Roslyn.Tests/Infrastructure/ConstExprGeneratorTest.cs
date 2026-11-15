using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ConstExpr.Roslyn.Generators;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal sealed class ConstExprGeneratorTest : CSharpSourceGeneratorTest<ConstExprGenerator, DefaultVerifier>
{
	internal ConstExprGeneratorTest()
	{
		ReferenceAssemblies = CompilationFixture.Framework;
		TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ConstExprAttribute).Assembly.Location));
		TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ExecutionResult).Assembly.Location));
	}

	protected override ParseOptions CreateParseOptions() => CompilationFixture.ParseOptions;
}
