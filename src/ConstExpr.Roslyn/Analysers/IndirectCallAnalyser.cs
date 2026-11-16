using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Analysers;

/// <summary>
/// Warns when a method reference bypasses compile-time evaluation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IndirectCallAnalyser : DiagnosticAnalyzer
{
#pragma warning disable RS2008 // Diagnostic release tracking is not configured.
	private static readonly DiagnosticDescriptor _indirectCall = new(
		"CONSTEXPR003", "Method reference bypasses compile-time evaluation",
		"Reference to '{0}' bypasses compile-time evaluation; invoking the delegate or function pointer will execute the original method at runtime",
		"ConstExpr", DiagnosticSeverity.Warning, true);
#pragma warning restore RS2008

	/// <inheritdoc />
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_indirectCall];

	/// <inheritdoc />
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterOperationAction(AnalyseMethodReference, OperationKind.MethodReference);
	}

	private static void AnalyseMethodReference(OperationAnalysisContext context)
	{
		IMethodReferenceOperation reference = (IMethodReferenceOperation)context.Operation;
		if (reference.Parent is not (IDelegateCreationOperation or IAddressOfOperation)
			|| !reference.Method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "ConstExpr.ConstExprAttribute"))
		{
			return;
		}
		context.ReportDiagnostic(Diagnostic.Create(_indirectCall, reference.Syntax.GetLocation(), reference.Method.ToDisplayString()));
	}
}
