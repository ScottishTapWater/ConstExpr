namespace ConstExpr.Roslyn.Evaluation;

internal sealed class EvaluatedResult
{
	internal EvaluatedResult(int id, string json)
	{
		Id = id;
		Json = json;
	}

	internal int Id { get; }
	internal string Json { get; }
}
