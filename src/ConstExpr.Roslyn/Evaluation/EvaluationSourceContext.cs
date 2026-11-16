using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Evaluation;

internal static class EvaluationSourceContext
{
	internal static string Capture(SemanticModel model, int position)
	{
		StringBuilder source = new("#pragma warning restore\n");
		foreach (SyntaxTrivia trivia in model.SyntaxTree.GetRoot().DescendantTrivia())
		{
			if (trivia.SpanStart >= position)
			{
				break;
			}
			if (trivia.GetStructure() is PragmaWarningDirectiveTriviaSyntax { IsActive: true } warning)
			{
				source.AppendLine(warning.ToFullString().TrimEnd('\r', '\n'));
			}
		}
		NullableContext context = model.GetNullableContext(position);
		source.AppendLine($"#nullable {(context.HasFlag(NullableContext.AnnotationsEnabled) ? "enable" : "disable")} annotations");
		source.AppendLine($"#nullable {(context.HasFlag(NullableContext.WarningsEnabled) ? "enable" : "disable")} warnings");
		return source.ToString();
	}

	internal static string LineDirective(Location location)
	{
		FileLinePositionSpan mapped = location.GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		return $"#line {mapped.StartLinePosition.Line + 1} {path}\n";
	}

	internal static ExpressionSyntax RestoreSourceEnd(ExpressionSyntax expression, ExpressionSyntax original)
	{
		FileLinePositionSpan mapped = original.GetLastToken().GetLocation().GetMappedLineSpan();
		string path = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(mapped.Path, quote: true);
		SyntaxTriviaList restore = SyntaxFactory.ParseLeadingTrivia($"\n#line {mapped.EndLinePosition.Line + 1} {path}\n");
		return expression.WithTrailingTrivia(restore.AddRange(original.GetTrailingTrivia()));
	}
}
