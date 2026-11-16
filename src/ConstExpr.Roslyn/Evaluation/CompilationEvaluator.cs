using System.Reflection;
using ConstExpr.Core.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class CompilationEvaluator
{
	private readonly CSharpCompilation _compilation;
	private readonly MethodEvaluator _evaluator;
	private readonly EvaluationAssemblyLoader _loader;
	private readonly CallExpressionCompiler _expressions;
	private readonly Dictionary<IMethodSymbol, Dictionary<ArgumentCacheKey, EvaluatedResult>> _results = new(SymbolEqualityComparer.Default);
	private Assembly? _assembly;
	private int _nextResultId;

	internal CompilationEvaluator(CSharpCompilation compilation, MethodEvaluator evaluator, EvaluationAssemblyLoader loader, CallExpressionCompiler expressions)
	{
		_compilation = compilation;
		_evaluator = evaluator;
		_loader = loader;
		_expressions = expressions;
	}

	internal EvaluatedResult EvaluateExpression(InvocationExpressionSyntax call, SemanticModel model, ITypeSymbol returnType, CancellationToken cancellationToken)
	{
		PreparedExpressionEvaluation prepared = _expressions.Prepare(call, model, returnType, cancellationToken);
		Assembly assembly = _loader.Load(prepared.Compilation, cancellationToken);
		MethodInfo entrypoint = assembly.GetTypes()
			.SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
			.Single(method => method.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == prepared.MarkerAttributeName));
		if (entrypoint.ContainsGenericParameters || entrypoint.GetParameters().Length != 0)
		{
			throw new InvalidOperationException("The expression requires a closed, parameterless evaluation function.");
		}
		object? value = _evaluator.Evaluate(entrypoint, []);
		string json = _loader.Serialize(value, entrypoint.ReturnType);
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
			_assembly ??= _loader.Load(_compilation.WithAssemblyName(_compilation.AssemblyName + ".ConstExprEvaluation"), cancellationToken);
			EvaluationMethodResolver resolver = new(_compilation.Assembly, _assembly, _loader);
			MethodInfo entrypoint = resolver.Resolve(method);
			ParameterInfo[] parameters = entrypoint.GetParameters();
			object?[] values = arguments.Select((value, index) => parameters[index].ParameterType.IsEnum
				? Enum.ToObject(parameters[index].ParameterType, value!) : value).ToArray();
			string json = _loader.Serialize(_evaluator.Evaluate(entrypoint, values), entrypoint.ReturnType);
			result = new(_nextResultId++, json);
			methodResults.Add(argumentKey, result);
		}
		return result;
	}
}
