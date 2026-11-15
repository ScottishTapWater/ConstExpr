using System.Text;
using ConstExpr.Roslyn.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConstExpr.Roslyn.Generation;

internal sealed class InterceptorSourceBuilder
{
	private readonly StringBuilder _methods = new();
	private readonly StringBuilder _fields = new();
	private readonly HashSet<int> _results = new();
	private int _count;

	internal bool HasInterceptors => _count > 0;

	internal void Add(InterceptableLocation location, IMethodSymbol method, EvaluatedResult result)
	{
		string returnType = FormatType(method.ReturnType);
		if (_results.Add(result.Id))
		{
			string json = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(result.Json, quote: true);
			_fields.AppendLine($"private static readonly {returnType} Result{result.Id} = global::System.Text.Json.JsonSerializer.Deserialize<{returnType}>({json}, Options)!;");
		}
		_methods.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")]");
		string signature = string.Join(", ", method.Parameters.Select(parameter => $"{FormatType(parameter.Type)} @{parameter.Name}"));
		_methods.AppendLine($"public static {returnType} Invoke{_count++}({signature}) => Result{result.Id};");
	}

	private static string FormatType(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
		.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

	internal string Build() => $$"""
		#nullable enable
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
				private static readonly global::System.Text.Json.JsonSerializerOptions Options = new()
				{
					NumberHandling = global::System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
					ReferenceHandler = global::System.Text.Json.Serialization.ReferenceHandler.Preserve,
				};
				{{_fields}}
				{{_methods}}
			}
		}
		""";
}
