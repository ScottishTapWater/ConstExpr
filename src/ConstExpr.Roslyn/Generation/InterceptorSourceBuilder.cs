using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Generation;

internal sealed class InterceptorSourceBuilder
{
	private readonly StringBuilder _methods = new();
	private int _count;

	internal bool HasInterceptors => _count > 0;

	internal void Add(InterceptableLocation location, ImmutableArray<IParameterSymbol> parameters, string literal)
	{
		_methods.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")]");
		string signature = string.Join(", ", parameters.Select(parameter => $"int @{parameter.Name}"));
		_methods.AppendLine($"public static int Invoke{_count++}({signature}) => {literal};");
	}

	internal string Build() => $$"""
		namespace System.Runtime.CompilerServices
		{
			[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
			file sealed class InterceptsLocationAttribute : System.Attribute
			{
				public InterceptsLocationAttribute(int version, string data) { }
			}
		}
		namespace ConstExpr.Generated
		{
			file static class Interceptors
			{
				{{_methods}}
			}
		}
		""";
}
