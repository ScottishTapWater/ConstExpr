using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Evaluation;

internal static class SourceTextPreserver
{
	internal static ExpressionSyntax Preserve(ExpressionSyntax replacement, SyntaxNode original)
	{
		// These build-only trees retain source spelling for caller information, while their AST carries
		// the resolved meaning. Compile them directly: printing and reparsing would discard that meaning.
		string text = original.ToString();
		SyntaxTrivia[] directives = original.DescendantTrivia().Where(trivia => trivia.IsDirective
			&& trivia.Span.Start >= original.Span.Start && trivia.Span.End <= original.Span.End).ToArray();
		string prefix = directives.Length == 0 ? text : text.Substring(0, directives[0].FullSpan.Start - original.SpanStart);
		string suffix = directives.Length == 0 ? "" : text.Substring(directives[directives.Length - 1].FullSpan.End - original.SpanStart);
		List<SyntaxTrivia> interior = [];
		for (int index = 0; index < directives.Length; index++)
		{
			interior.Add(directives[index]);
			if (index + 1 < directives.Length)
			{
				int start = directives[index].FullSpan.End - original.SpanStart;
				int end = directives[index + 1].FullSpan.Start - original.SpanStart;
				interior.Add(SyntaxFactory.Whitespace(text.Substring(start, end - start)));
			}
		}
		SyntaxToken first = replacement.GetFirstToken();
		SyntaxToken last = replacement.GetLastToken();
		if (first == last && directives.Length != 0)
		{
			throw new InvalidOperationException("A source expression containing directives requires an interior syntax boundary.");
		}
		ExpressionSyntax result = replacement.ReplaceTokens(replacement.DescendantTokens(), (token, _) => WithText(token,
			token == first ? prefix : token == last ? suffix : "",
			token == first ? original.GetLeadingTrivia() : token == last ? SyntaxFactory.TriviaList(interior) : default,
			token == last ? original.GetTrailingTrivia() : default));
		if (result.ToFullString() != original.ToFullString())
		{
			throw new InvalidOperationException("Extraction changed the original expression text.");
		}
		return result;
	}

	private static SyntaxToken WithText(SyntaxToken token, string text, SyntaxTriviaList leading, SyntaxTriviaList trailing)
	{
		if (token.IsKind(SyntaxKind.IdentifierToken))
		{
			return SyntaxFactory.Identifier(leading, SyntaxKind.IdentifierToken, text, token.ValueText, trailing);
		}
		if (token.IsKind(SyntaxKind.StringLiteralToken))
		{
			return SyntaxFactory.Literal(leading, text, (string)token.Value!, trailing);
		}
		if (token.IsKind(SyntaxKind.CharacterLiteralToken))
		{
			return SyntaxFactory.Literal(leading, text, (char)token.Value!, trailing);
		}
		if (token.IsKind(SyntaxKind.NumericLiteralToken))
		{
			return token.Value switch
			{
				int value => SyntaxFactory.Literal(leading, text, value, trailing),
				uint value => SyntaxFactory.Literal(leading, text, value, trailing),
				long value => SyntaxFactory.Literal(leading, text, value, trailing),
				ulong value => SyntaxFactory.Literal(leading, text, value, trailing),
				float value => SyntaxFactory.Literal(leading, text, value, trailing),
				double value => SyntaxFactory.Literal(leading, text, value, trailing),
				decimal value => SyntaxFactory.Literal(leading, text, value, trailing),
				_ => throw new InvalidOperationException("Unsupported numeric literal in extracted expression.")
			};
		}
		return SyntaxFactory.Token(leading, token.Kind(), text, token.ValueText, trailing);
	}
}
