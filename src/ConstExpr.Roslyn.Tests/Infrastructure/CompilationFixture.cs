using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class CompilationFixture
{
	internal static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp14)
		.WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "ConstExpr.Generated")]);

	internal static readonly ReferenceAssemblies Framework = new(
		"net10.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"), Path.Combine("ref", "net10.0"));

	private static readonly Task<ImmutableArray<MetadataReference>> _references = Framework.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

	internal static async Task<CSharpCompilation> Create(params SourceFile[] sources)
	{
		ImmutableArray<MetadataReference> references = await _references;
		CSharpCompilation compilation = CSharpCompilation.Create(
			$"Fixture_{Guid.NewGuid():N}",
			sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, ParseOptions, source.Path)),
			references.Add(MetadataReference.CreateFromFile(typeof(ConstExprAttribute).Assembly.Location))
				.Add(MetadataReference.CreateFromFile(typeof(ExecutionResult).Assembly.Location)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		ThrowOnErrors(compilation.GetDiagnostics());
		return compilation;
	}

	internal static void ThrowOnErrors(IEnumerable<Diagnostic> diagnostics)
	{
		Diagnostic[] errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
		if (errors.Length != 0)
		{
			throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
		}
	}
}
