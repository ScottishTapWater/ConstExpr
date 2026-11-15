using System.IO;
using System.Reflection;
using ConstExpr.Core.Evaluation;
using ConstExpr.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CompilationEvaluator
{
	private readonly CSharpCompilation _compilation;
	private readonly MethodEvaluator _evaluator;
	private readonly ResultSerializer _serializer;
	private readonly EvaluationAssemblyLoader _loader;
	private readonly CallExpressionCompiler _expressions;
	private readonly Dictionary<IMethodSymbol, Dictionary<ArgumentCacheKey, EvaluatedResult>> _results = new(SymbolEqualityComparer.Default);
	private Assembly? _assembly;
	private int _nextResultId;

	internal CompilationEvaluator(CSharpCompilation compilation, MethodEvaluator evaluator, ResultSerializer serializer, EvaluationAssemblyLoader loader, CallExpressionCompiler expressions)
	{
		_compilation = compilation;
		_evaluator = evaluator;
		_serializer = serializer;
		_loader = loader;
		_expressions = expressions;
	}

	internal EvaluatedResult EvaluateExpression(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType, CancellationToken cancellationToken)
	{
		MethodInfo entrypoint = _expressions.Compile(call, model, returnType, cancellationToken);
		object? value = _evaluator.Evaluate(entrypoint, []);
		string json = _serializer.Serialize(value, entrypoint.ReturnType);
		return new(_nextResultId++, json);
	}

	internal EvaluatedResult Evaluate(IMethodSymbol method, object?[] arguments, CancellationToken cancellationToken)
	{
		if (!_results.TryGetValue(method, out Dictionary<ArgumentCacheKey, EvaluatedResult>? methodResults))
		{
			methodResults = new();
			_results.Add(method, methodResults);
		}
		ArgumentCacheKey argumentKey = new(arguments);
		if (!methodResults.TryGetValue(argumentKey, out EvaluatedResult? result))
		{
			_assembly ??= Compile(cancellationToken);
			Type type = _assembly.GetType(GetMetadataName(method.ContainingType), throwOnError: true)!;
			MethodInfo entrypoint = FindMethod(type, method);
			ParameterInfo[] parameters = entrypoint.GetParameters();
			object?[] values = arguments.Select((value, index) => parameters[index].ParameterType.IsEnum
				? Enum.ToObject(parameters[index].ParameterType, value!) : value).ToArray();
			string json = _serializer.Serialize(_evaluator.Evaluate(entrypoint, values), entrypoint.ReturnType);
			result = new(_nextResultId++, json);
			methodResults.Add(argumentKey, result);
		}
		return result;
	}

	private static MethodInfo FindMethod(Type type, IMethodSymbol method)
	{
		return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
			.Single(candidate => candidate.Name == method.MetadataName && !candidate.IsGenericMethod
				&& candidate.GetParameters().Select(parameter => parameter.ParameterType.FullName)
					.SequenceEqual(method.Parameters.Select(parameter => GetMetadataName((INamedTypeSymbol)parameter.Type))));
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
		return _loader.Load(stream.ToArray());
	}

	private static string GetMetadataName(INamedTypeSymbol type)
	{
		return type.ContainingType is not null
			? GetMetadataName(type.ContainingType) + "+" + type.MetadataName
			: type.ContainingNamespace.IsGlobalNamespace ? type.MetadataName
				: type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
	}
}
