using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class PreparedExpressionEvaluation
{
	internal PreparedExpressionEvaluation(CSharpCompilation compilation, string markerAttributeName)
	{
		Compilation = compilation;
		MarkerAttributeName = markerAttributeName;
	}

	internal CSharpCompilation Compilation { get; }
	internal string MarkerAttributeName { get; }
}
