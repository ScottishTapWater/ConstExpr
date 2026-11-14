using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Tests.Infrastructure;

internal static class ManualInterceptor
{
	internal static CSharpCompilation Add(CSharpCompilation compilation)
	{
		SyntaxTree caller = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Caller.cs");
		InvocationExpressionSyntax call = caller.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
			.Single(invocation => invocation.Expression.ToString() == "Counter.CountUp");
		InterceptableLocation location = compilation.GetSemanticModel(caller).GetInterceptableLocation(call)
			?? throw new InvalidOperationException("The control fixture must contain an interceptable call.");
		string source = $$"""
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
				file static class HandWrittenInterceptor
				{
					[System.Runtime.CompilerServices.InterceptsLocation({{location.Version}}, "{{location.Data}}")]
					public static int Invoke() => 7;
				}
			}
			""";
		return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, CompilationFixture.ParseOptions, "ControlInterceptor.cs"));
	}
}
