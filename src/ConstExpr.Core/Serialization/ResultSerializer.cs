using System.Text.Json;

namespace ConstExpr.Core.Serialization;

internal sealed class ResultSerializer
{
	public string Serialize(object? value, Type type)
	{
		return JsonSerializer.Serialize(value, type);
	}

	public object? Deserialize(string value, Type type)
	{
		return JsonSerializer.Deserialize(value, type);
	}
}
