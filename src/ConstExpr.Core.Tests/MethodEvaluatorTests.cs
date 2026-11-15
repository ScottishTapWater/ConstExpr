using System.Reflection;
using ConstExpr.Core.Evaluation;

namespace ConstExpr.Core.Tests;

public sealed class MethodEvaluatorTests
{
	[Test]
	public async Task EvaluationExecutesTheSuppliedMethodAndReturnsItsResult()
	{
		MethodInfo method = typeof(EvaluationFixture).GetMethod(nameof(EvaluationFixture.ReturnValue))!;
		MethodEvaluator evaluator = new();

		object? result = evaluator.Evaluate(method);

		await Assert.That(result).IsEqualTo(1);
	}

	private static class EvaluationFixture
	{
		public static int ReturnValue() => 1;
	}
}
