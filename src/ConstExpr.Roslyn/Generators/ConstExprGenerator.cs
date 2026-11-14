using Microsoft.CodeAnalysis;

namespace ConstExpr.Roslyn.Generators;

/// <summary>
/// Provides the ConstExpr incremental source generator entry point.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConstExprGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
	}
}
