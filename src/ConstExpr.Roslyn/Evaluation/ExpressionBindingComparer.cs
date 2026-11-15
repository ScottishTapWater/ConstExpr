using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class ExpressionBindingComparer
{
	private readonly IOperation _original;
	private readonly IOperation _extracted;
	private readonly CancellationToken _cancellationToken;
	private readonly Dictionary<IOperation, IOperation> _operations = new();
	private readonly Dictionary<ISymbol, ISymbol> _symbols = new(SymbolEqualityComparer.Default);
	private readonly HashSet<ISymbol> _mapped = new(SymbolEqualityComparer.Default);
	private string _difference = "expression";

	private ExpressionBindingComparer(IOperation original, IOperation extracted, CancellationToken cancellationToken)
	{
		_original = original;
		_extracted = extracted;
		_cancellationToken = cancellationToken;
	}

	internal static void Verify(IOperation original, IOperation extracted, CancellationToken cancellationToken)
	{
		ExpressionBindingComparer comparison = new(original, extracted, cancellationToken);
		if (!comparison.CompareOperation(original, extracted, "expression"))
		{
			throw new InvalidOperationException($"Extraction changed compiler binding at '{comparison._difference}'.");
		}
	}

	private bool CompareOperation(IOperation original, IOperation extracted, string path)
	{
		_cancellationToken.ThrowIfCancellationRequested();
		_difference = path;
		if (_operations.TryGetValue(original, out IOperation? mapped))
		{
			return ReferenceEquals(mapped, extracted);
		}
		_operations.Add(original, extracted);
		if (!CompareType(original.Type, extracted.Type))
		{
			_difference = path + $" (type {original.Type} [{original.Type?.NullableAnnotation}] versus {extracted.Type} [{extracted.Type?.NullableAnnotation}])";
			return false;
		}
		if (original.ConstantValue.HasValue || extracted.ConstantValue.HasValue)
		{
			bool equal = original.ConstantValue.HasValue && extracted.ConstantValue.HasValue
				&& CompareConstant(original.ConstantValue.Value, extracted.ConstantValue.Value);
			if (!equal)
			{
				_difference = path + $" (constant {original.Kind}/{original.ConstantValue.HasValue} versus {extracted.Kind}/{extracted.ConstantValue.HasValue})";
			}
			return equal;
		}
		if (original.Kind != extracted.Kind || original.IsImplicit != extracted.IsImplicit)
		{
			_difference = path + $" (operation {original.Kind}/{original.IsImplicit} versus {extracted.Kind}/{extracted.IsImplicit})";
			return false;
		}
		// Inspect the public semantic contract, including implicit calls and compiler-supplied arguments.
		// New operation interfaces/properties must compare successfully before extracted code can run.
		Type[] contracts = original.GetType().GetInterfaces()
			.Where(type => type != typeof(IOperation) && typeof(IOperation).IsAssignableFrom(type)).ToArray();
		foreach (Type contract in contracts)
		{
			if (!contract.IsInstanceOfType(extracted))
			{
				return false;
			}
			foreach (PropertyInfo property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
			{
				if (!CompareValue(property.GetValue(original), property.GetValue(extracted), path + "." + property.Name))
				{
					return false;
				}
			}
		}
		return CompareValue(original.ChildOperations, extracted.ChildOperations, path + ".Children");
	}

	private bool CompareValue(object? original, object? extracted, string path)
	{
		_difference = path;
		if (original is null || extracted is null)
		{
			return original is null && extracted is null;
		}
		return (original, extracted) switch
		{
			(IOperation first, IOperation second) => CompareOperation(first, second, path),
			(ITypeSymbol first, ITypeSymbol second) => CompareType(first, second),
			(ISymbol first, ISymbol second) => CompareSymbol(first, second),
			(CommonConversion first, CommonConversion second) => first.Exists == second.Exists && first.IsIdentity == second.IsIdentity
				&& first.IsImplicit == second.IsImplicit && first.IsNumeric == second.IsNumeric && first.IsReference == second.IsReference
				&& first.IsNullable == second.IsNullable && first.IsUserDefined == second.IsUserDefined
				&& CompareSymbol(first.MethodSymbol, second.MethodSymbol) && CompareType(first.ConstrainedToType, second.ConstrainedToType),
			(string first, string second) => first == second,
			(IEnumerable first, IEnumerable second) => CompareSequence(first, second, path),
			_ when original.GetType().IsEnum || original is bool or int or uint or long or ulong or char or byte or sbyte or short or ushort => original.Equals(extracted),
			_ => false
		};
	}

	private bool CompareSequence(IEnumerable original, IEnumerable extracted, string path)
	{
		IEnumerator first = original.GetEnumerator();
		IEnumerator second = extracted.GetEnumerator();
		try
		{
			int index = 0;
			while (first.MoveNext())
			{
				if (!second.MoveNext() || !CompareValue(first.Current, second.Current, path + "[" + index++ + "]"))
				{
					return false;
				}
			}
			return !second.MoveNext();
		}
		finally
		{
			(first as IDisposable)?.Dispose();
			(second as IDisposable)?.Dispose();
		}
	}

	private bool CompareType(ITypeSymbol? original, ITypeSymbol? extracted)
	{
		if (original is null || extracted is null)
		{
			return original is null && extracted is null;
		}
		if (original.TypeKind != extracted.TypeKind || original.NullableAnnotation != extracted.NullableAnnotation)
		{
			return false;
		}
		return (original, extracted) switch
		{
			(ITypeParameterSymbol first, ITypeParameterSymbol second) => CompareSymbol(first, second),
			(IArrayTypeSymbol first, IArrayTypeSymbol second) => first.Rank == second.Rank && first.IsSZArray == second.IsSZArray && CompareType(first.ElementType, second.ElementType),
			(IPointerTypeSymbol first, IPointerTypeSymbol second) => CompareType(first.PointedAtType, second.PointedAtType),
			(IFunctionPointerTypeSymbol first, IFunctionPointerTypeSymbol second) => CompareSignature(first.Signature, second.Signature),
			(INamedTypeSymbol { IsAnonymousType: true } first, INamedTypeSymbol { IsAnonymousType: true } second) =>
				CompareSequence(first.GetMembers().OfType<IPropertySymbol>().Select(property => property.Name), second.GetMembers().OfType<IPropertySymbol>().Select(property => property.Name), "anonymous properties")
				&& CompareSequence(first.GetMembers().OfType<IPropertySymbol>().Select(property => property.Type), second.GetMembers().OfType<IPropertySymbol>().Select(property => property.Type), "anonymous types"),
			(INamedTypeSymbol first, INamedTypeSymbol second) => first.MetadataName == second.MetadataName
				&& first.ContainingNamespace.ToDisplayString() == second.ContainingNamespace.ToDisplayString()
				&& CompareAssembly(first.ContainingAssembly, second.ContainingAssembly)
				&& CompareType(first.ContainingType, second.ContainingType) && CompareSequence(first.TypeArguments, second.TypeArguments, "type arguments"),
			_ => original.SpecialType == extracted.SpecialType
		};
	}

	private bool CompareSymbol(ISymbol? original, ISymbol? extracted)
	{
		if (original is null || extracted is null)
		{
			return original is null && extracted is null;
		}
		if (original.Kind != extracted.Kind || original.MetadataName != extracted.MetadataName)
		{
			return false;
		}
		if (original is IDiscardSymbol firstDiscard && extracted is IDiscardSymbol secondDiscard)
		{
			return CompareType(firstDiscard.Type, secondDiscard.Type);
		}
		if (IsInternal(original, _original.Syntax) && IsInternal(extracted, _extracted.Syntax))
		{
			if (_symbols.TryGetValue(original, out ISymbol? mapped))
			{
				return SymbolEqualityComparer.Default.Equals(mapped, extracted);
			}
			if (!_mapped.Add(extracted))
			{
				return false;
			}
			_symbols.Add(original, extracted);
			return (original, extracted) switch
			{
				(IMethodSymbol first, IMethodSymbol second) => CompareSignature(first, second),
				(ITypeParameterSymbol first, ITypeParameterSymbol second) => first.Ordinal == second.Ordinal
					&& first.HasValueTypeConstraint == second.HasValueTypeConstraint && first.HasReferenceTypeConstraint == second.HasReferenceTypeConstraint
					&& first.HasConstructorConstraint == second.HasConstructorConstraint && first.HasUnmanagedTypeConstraint == second.HasUnmanagedTypeConstraint
					&& CompareSequence(first.ConstraintTypes, second.ConstraintTypes, "constraints"),
				(ILocalSymbol first, ILocalSymbol second) => first.RefKind == second.RefKind && CompareType(first.Type, second.Type),
				(IParameterSymbol first, IParameterSymbol second) => first.Ordinal == second.Ordinal && first.RefKind == second.RefKind && CompareType(first.Type, second.Type),
				(IPropertySymbol first, IPropertySymbol second) => first.RefKind == second.RefKind && first.IsReadOnly == second.IsReadOnly
					&& CompareType(first.ContainingType, second.ContainingType) && CompareType(first.Type, second.Type),
				(IFieldSymbol first, IFieldSymbol second) => first.RefKind == second.RefKind && first.IsReadOnly == second.IsReadOnly
					&& first.CorrespondingTupleField?.MetadataName == second.CorrespondingTupleField?.MetadataName
					&& CompareType(first.ContainingType, second.ContainingType) && CompareType(first.Type, second.Type),
				(ILabelSymbol, ILabelSymbol) => true,
				_ => false
			};
		}
		if (original.GetDocumentationCommentId() != extracted.GetDocumentationCommentId()
			|| !CompareAssembly(original.ContainingAssembly, extracted.ContainingAssembly)
			|| !CompareType(original.ContainingType, extracted.ContainingType))
		{
			return false;
		}
		return (original, extracted) switch
		{
			(IMethodSymbol first, IMethodSymbol second) => CompareSequence(first.TypeArguments, second.TypeArguments, "method arguments"),
			(IParameterSymbol first, IParameterSymbol second) => first.Ordinal == second.Ordinal && CompareType(first.Type, second.Type) && CompareSymbol(first.ContainingSymbol, second.ContainingSymbol),
			(IPropertySymbol first, IPropertySymbol second) => CompareType(first.Type, second.Type),
			(IFieldSymbol first, IFieldSymbol second) => CompareType(first.Type, second.Type),
			_ => original.GetDocumentationCommentId() is not null
		};
	}

	private bool CompareSignature(IMethodSymbol original, IMethodSymbol extracted) => original.MethodKind == extracted.MethodKind
		&& original.RefKind == extracted.RefKind && original.CallingConvention == extracted.CallingConvention
		&& CompareType(original.ReturnType, extracted.ReturnType)
		&& CompareSequence(original.TypeArguments, extracted.TypeArguments, "signature arguments")
		&& CompareSequence(original.Parameters.Select(parameter => parameter.Type), extracted.Parameters.Select(parameter => parameter.Type), "signature parameters")
		&& original.Parameters.Select(parameter => parameter.RefKind).SequenceEqual(extracted.Parameters.Select(parameter => parameter.RefKind));

	private bool CompareAssembly(IAssemblySymbol? original, IAssemblySymbol? extracted) =>
		SymbolEqualityComparer.Default.Equals(original, _original.SemanticModel?.Compilation.Assembly)
		&& SymbolEqualityComparer.Default.Equals(extracted, _extracted.SemanticModel?.Compilation.Assembly)
		|| Equals(original?.Identity, extracted?.Identity);

	private static bool IsInternal(ISymbol symbol, SyntaxNode root) => symbol is ILabelSymbol
		|| symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction }
		|| symbol.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree == root.SyntaxTree && root.Span.Contains(reference.Span))
		|| symbol is IParameterSymbol or ITypeParameterSymbol && symbol.ContainingSymbol is IMethodSymbol method && IsInternal(method, root);

	private static bool CompareConstant(object? original, object? extracted) => (original, extracted) switch
	{
		(double first, double second) => BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second),
		(float first, float second) => BitConverter.GetBytes(first).SequenceEqual(BitConverter.GetBytes(second)),
		(decimal first, decimal second) => decimal.GetBits(first).SequenceEqual(decimal.GetBits(second)),
		_ => Equals(original, extracted)
	};
}
