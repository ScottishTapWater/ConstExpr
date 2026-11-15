using ConstExpr.Roslyn.Tests.Infrastructure;

namespace ConstExpr.Roslyn.Tests;

public sealed class CustomConverterTests
{
	[Test]
	public async Task JsonConverterAttributePreservesPrivateStateAcrossBuildTimeSerialisationAndRuntimeReconstruction()
	{
		const string source = """
			using System;
			using System.Text.Json;
			using System.Text.Json.Serialization;
			using ConstExpr;
			using ConstExpr.Roslyn.Tests;

			[JsonConverter(typeof(SecretConverter))]
			public sealed class Secret
			{
				private readonly int value;

				public Secret(int value) { this.value = value; }
				public int ReadValue() => value;
			}

			public sealed class SecretConverter : JsonConverter<Secret>
			{
				public static int Reads;
				public static int Writes;

				public override Secret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
				{
					Reads++;
					return new(reader.GetInt32() - 100);
				}

				public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options)
				{
					Writes++;
					writer.WriteNumberValue(value.ReadValue() + 100);
				}
			}

			public static class Factory
			{
				private static int calls;

				[ConstExpr]
				public static Secret Create()
				{
					calls++;
					return new(42);
				}

				public static CustomConverterTests.ConverterObservation Run() =>
					new(Create().ReadValue(), calls, SecretConverter.Reads, SecretConverter.Writes);
			}
			""";

		ConverterObservation result = await ConstExprFixture.CompileAndRun<ConverterObservation>(
			[new("Program.cs", source)], "Factory.Run");

		await Assert.That(result).IsEqualTo(new ConverterObservation(42, 0, 1, 0));
	}

	public sealed record ConverterObservation(int Value, int RuntimeMethodCalls, int RuntimeReads, int RuntimeWrites);
}
