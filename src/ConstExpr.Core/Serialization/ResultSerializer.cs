using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConstExpr.Core.Serialization;

internal sealed class ResultSerializer
{
	private readonly JsonSerializerOptions _options = new()
	{
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		ReferenceHandler = ReferenceHandler.Preserve,
	};

	public string Serialize(object? value, Type type)
	{
		return JsonSerializer.Serialize(value, type, _options);
	}

	public object? Deserialize(string value, Type type)
	{
		return JsonSerializer.Deserialize(value, type, _options);
	}
}
