using System.Globalization;

namespace ConstExpr.Core.Serialization;

internal sealed class ResultSerializer
{
	public string Serialize(object? value, Type type)
	{
		if (type != typeof(int))
		{
			throw new NotSupportedException("Only int results are supported.");
		}
		return ((int)value!).ToString(CultureInfo.InvariantCulture);
	}

	public object? Deserialize(string value, Type type)
	{
		if (type != typeof(int))
		{
			throw new NotSupportedException("Only int results are supported.");
		}
		return int.Parse(value, CultureInfo.InvariantCulture);
	}
}
