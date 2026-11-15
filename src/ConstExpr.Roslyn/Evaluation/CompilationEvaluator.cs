using System.IO;
using System.Reflection;
using ConstExpr.Core.Evaluation;
using ConstExpr.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CompilationEvaluator
{
	private readonly CSharpCompilation _compilation;
	private readonly MethodEvaluator _evaluator;
	private readonly ResultSerializer _serializer;
	private readonly Dictionary<IMethodSymbol, Dictionary<string, EvaluatedResult>> _results = new(SymbolEqualityComparer.Default);
	private Assembly? _assembly;
	private int _nextResultId;

	internal CompilationEvaluator(CSharpCompilation compilation, MethodEvaluator evaluator, ResultSerializer serializer)
	{
		_compilation = compilation;
		_evaluator = evaluator;
		_serializer = serializer;
	}

	internal EvaluatedResult Evaluate(IMethodSymbol method, int[] arguments, CancellationToken cancellationToken)
	{
		if (!_results.TryGetValue(method, out Dictionary<string, EvaluatedResult>? methodResults))
		{
			methodResults = new();
			_results.Add(method, methodResults);
		}
		string argumentKey = _serializer.Serialize(arguments, typeof(int[]));
		if (!methodResults.TryGetValue(argumentKey, out EvaluatedResult? result))
		{
			_assembly ??= Compile(cancellationToken);
			Type type = _assembly.GetType(GetMetadataName(method.ContainingType), throwOnError: true)!;
			Type[] parameterTypes = Enumerable.Repeat(typeof(int), arguments.Length).ToArray();
			MethodInfo entrypoint = type.GetMethod(method.MetadataName,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null)!;
			object?[] values = arguments.Select(value => (object?)value).ToArray();
			string json = _serializer.Serialize(_evaluator.Evaluate(entrypoint, values), entrypoint.ReturnType);
			result = new(_nextResultId++, json);
			methodResults.Add(argumentKey, result);
		}
		return result;
	}

	private Assembly Compile(CancellationToken cancellationToken)
	{
		using MemoryStream stream = new();
		EmitResult result = _compilation.WithAssemblyName(_compilation.AssemblyName + ".ConstExprEvaluation")
			.Emit(stream, cancellationToken: cancellationToken);
		if (!result.Success)
		{
			throw new InvalidOperationException(string.Join("\n",
				result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
		}
#pragma warning disable RS1035 // Executing the user's method is the purpose of this generator.
		return Assembly.Load(stream.ToArray());
#pragma warning restore RS1035
	}

	private static string GetMetadataName(INamedTypeSymbol type)
	{
		return type.ContainingType is not null
			? GetMetadataName(type.ContainingType) + "+" + type.MetadataName
			: type.ToDisplayString();
	}
}
