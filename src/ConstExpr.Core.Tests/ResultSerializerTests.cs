using ConstExpr.Core.Serialization;

namespace ConstExpr.Core.Tests;

public sealed class ResultSerializerTests
{
	[Test]
	public async Task RoundTripPreservesOne()
	{
		ResultSerializer serializer = new();

		string serialized = serializer.Serialize(1, typeof(int));
		object? result = serializer.Deserialize(serialized, typeof(int));

		await Assert.That(result).IsEqualTo(1);
	}
}
