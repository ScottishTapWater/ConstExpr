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

	[Test]
	public async Task EvaluationPassesTheSuppliedArgumentToTheMethod()
	{
		MethodInfo method = typeof(EvaluationFixture).GetMethod(nameof(EvaluationFixture.AddOne))!;
		MethodEvaluator evaluator = new();

		object? result = evaluator.Evaluate(method, 41);

		await Assert.That(result).IsEqualTo(42);
	}

	private static class EvaluationFixture
	{
		public static int ReturnValue() => 1;
		public static int AddOne(int value) => value + 1;
	}
}
