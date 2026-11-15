using System.Collections.Immutable;

namespace ConstExpr.Roslyn.Evaluation;

internal sealed class ArgumentCacheKey : IEquatable<ArgumentCacheKey>
{
	private readonly ImmutableArray<object?> _values;

	internal ArgumentCacheKey(IEnumerable<object?> values)
	{
		_values = values.ToImmutableArray();
	}

	public bool Equals(ArgumentCacheKey? other)
	{
		if (other is null || _values.Length != other._values.Length)
		{
			return false;
		}
		for (int index = 0; index < _values.Length; index++)
		{
			if (!SameValue(_values[index], other._values[index]))
			{
				return false;
			}
		}
		return true;
	}

	public override bool Equals(object? obj) => obj is ArgumentCacheKey other && Equals(other);

	public override int GetHashCode()
	{
		int hash = 17;
		foreach (object? value in _values)
		{
			hash = unchecked(hash * 31 + (value?.GetHashCode() ?? 0));
		}
		return hash;
	}

	private static bool SameValue(object? left, object? right) => left switch
	{
		double value when right is double other => BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits(other),
		float value when right is float other => BitConverter.GetBytes(value).SequenceEqual(BitConverter.GetBytes(other)),
		decimal value when right is decimal other => decimal.GetBits(value).SequenceEqual(decimal.GetBits(other)),
		_ => object.Equals(left, right),
	};
}
