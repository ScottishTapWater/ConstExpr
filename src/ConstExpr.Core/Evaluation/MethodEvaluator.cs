using System.Reflection;

namespace ConstExpr.Core.Evaluation;

internal sealed class MethodEvaluator
{
	public object? Evaluate(MethodInfo method, params object?[] arguments)
	{
		return method.Invoke(null, arguments);
	}
}
