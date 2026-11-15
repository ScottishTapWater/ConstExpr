using System.Reflection;

namespace ConstExpr.Core.Evaluation;

internal sealed class MethodEvaluator
{
	public object? Evaluate(MethodInfo method)
	{
		return method.Invoke(null, null);
	}
}
