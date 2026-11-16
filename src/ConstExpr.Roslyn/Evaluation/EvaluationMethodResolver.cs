using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class EvaluationMethodResolver
{
	private readonly IAssemblySymbol _source;
	private readonly Assembly _assembly;
	private readonly EvaluationAssemblyLoader _loader;

	internal EvaluationMethodResolver(IAssemblySymbol source, Assembly assembly, EvaluationAssemblyLoader loader)
	{
		_source = source;
		_assembly = assembly;
		_loader = loader;
	}

	internal MethodInfo Resolve(IMethodSymbol method)
	{
		Type type = ResolveType(method.ContainingType);
		IMethodSymbol definition = method.OriginalDefinition;
		// Select the definition first: substitution can make distinct overload signatures identical.
		MethodInfo entrypoint = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
			.Single(candidate => candidate.Name == definition.MetadataName && candidate.GetGenericArguments().Length == definition.Arity
				&& MatchesParameters(candidate, method.ConstructedFrom));
		return method.IsGenericMethod ? entrypoint.MakeGenericMethod(method.TypeArguments.Select(ResolveType).ToArray()) : entrypoint;
	}

	private bool MatchesParameters(MethodInfo candidate, IMethodSymbol method)
	{
		ParameterInfo[] parameters = candidate.GetParameters();
		return parameters.Length == method.Parameters.Length && parameters.Select((parameter, index) =>
			MatchesType(parameter.ParameterType, method.Parameters[index].Type, candidate.GetGenericArguments())).All(matches => matches);
	}

	private bool MatchesType(Type runtime, ITypeSymbol symbol, Type[] methodArguments)
	{
		if (symbol is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter)
		{
			return runtime == methodArguments[parameter.Ordinal];
		}
		if (runtime.IsGenericParameter && runtime.DeclaringMethod is not null)
		{
			return false;
		}
		return symbol switch
		{
			IArrayTypeSymbol array => runtime.IsArray && runtime.GetArrayRank() == array.Rank
				&& MatchesType(runtime.GetElementType()!, array.ElementType, methodArguments),
			INamedTypeSymbol named when runtime.IsGenericType => runtime.GetGenericTypeDefinition() == ResolveDefinition(named)
				&& runtime.GetGenericArguments().Zip(GetTypeArguments(named), (argument, type) => MatchesType(argument, type, methodArguments)).All(matches => matches),
			_ => runtime == ResolveType(symbol)
		};
	}

	private Type ResolveType(ITypeSymbol symbol)
	{
		if (symbol is IDynamicTypeSymbol)
		{
			return typeof(object);
		}
		if (symbol is IArrayTypeSymbol array)
		{
			Type element = ResolveType(array.ElementType);
			return array.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(array.Rank);
		}
		INamedTypeSymbol named = (INamedTypeSymbol)symbol;
		Type definition = ResolveDefinition(named);
		return definition.IsGenericTypeDefinition ? definition.MakeGenericType(GetTypeArguments(named).Select(ResolveType).ToArray()) : definition;
	}

	private Type ResolveDefinition(INamedTypeSymbol named)
	{
		Assembly assembly = SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, _source)
			? _assembly : _loader.LoadRuntimeAssembly(new AssemblyName(named.ContainingAssembly.Identity.ToString()));
		return assembly.GetType(GetMetadataName(named), throwOnError: true)!;
	}

	private static IEnumerable<ITypeSymbol> GetTypeArguments(INamedTypeSymbol type) =>
		(type.ContainingType is null ? Enumerable.Empty<ITypeSymbol>() : GetTypeArguments(type.ContainingType)).Concat(type.TypeArguments);

	private static string GetMetadataName(INamedTypeSymbol type) => type.ContainingType is not null
		? GetMetadataName(type.ContainingType) + "+" + type.MetadataName
		: type.ContainingNamespace.IsGlobalNamespace ? type.MetadataName : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
}
