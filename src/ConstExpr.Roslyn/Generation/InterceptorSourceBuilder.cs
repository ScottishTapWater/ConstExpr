using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Generation;

internal sealed class InterceptorSourceBuilder
{
	private readonly StringBuilder _methods = new();
	private int _count;

	internal bool HasInterceptors => _count > 0;

	internal void Add(InterceptableLocation location, int parameterCount, string literal)
	{
		_methods.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")]");
		string parameters = parameterCount == 0 ? "" : "int value";
		_methods.AppendLine($"public static int Invoke{_count++}({parameters}) => {literal};");
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
