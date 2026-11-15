using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Evaluation;

internal static class GenericContextTypes
{
	internal static string Find(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
	{
		bool requiresContext = false;
		for (ISymbol? symbol = model.GetEnclosingSymbol(call.SpanStart, cancellationToken); symbol is not null; symbol = symbol.ContainingSymbol)
		{
			requiresContext |= symbol is IMethodSymbol { IsGenericMethod: true };
		}
		if (!requiresContext)
		{
			return "";
		}

		HashSet<ITypeSymbol> types = new(SymbolEqualityComparer.Default);
		INamedTypeSymbol within = model.GetEnclosingSymbol(call.SpanStart, cancellationToken)!.ContainingType!;
		foreach (SyntaxTree tree in model.Compilation.SyntaxTrees)
		{
			SemanticModel callerModel = model.Compilation.GetSemanticModel(tree);
			foreach (GenericNameSyntax name in tree.GetRoot(cancellationToken).DescendantNodes().OfType<GenericNameSyntax>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				IEnumerable<ITypeSymbol> candidates = callerModel.GetSymbolInfo(name, cancellationToken).Symbol switch
				{
					IMethodSymbol method => method.TypeArguments,
					INamedTypeSymbol type => [type],
					_ => []
				};
				types.UnionWith(candidates.Where(type => !ArgumentExpressionAnalysis.ContainsTypeParameter(type)
					&& type.TypeKind != TypeKind.Dynamic && !type.IsRefLikeType && model.Compilation.IsSymbolAccessibleWithin(type, within)));
			}
		}
		return string.Concat(types.Select(type => ", typeof(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")"));
	}
}
