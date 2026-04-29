// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

using Elastic.Esql.Serialization;
using Elastic.Esql.Vectors;

namespace Elastic.Esql.Tests.TypeMapping.Vectors;

public class FloatVectorConverterTests
{
	[Test]
	public void Serialize_LegacyEncoding_WritesJsonArray()
	{
		var options = OptionsWith(FloatVectorEncoding.Legacy);
		var vec = new FloatVector(new float[] { 0.1f, 0.2f, 0.3f });

		var json = JsonSerializer.Serialize(vec, options);

		_ = json.Should().Be("[0.1,0.2,0.3]");
	}

	[Test]
	public void Serialize_Base64Encoding_WritesBase64String()
	{
		var options = OptionsWith(FloatVectorEncoding.Base64);
		var vec = new FloatVector(new float[] { 1f, 2f, 3f });

		var json = JsonSerializer.Serialize(vec, options);

		_ = json.Should().StartWith("\"").And.EndWith("\"");
		_ = json.Length.Should().BeGreaterThan(2);
	}

	[Test]
	public void RoundTrip_LegacyEncoding_PreservesValues()
	{
		var options = OptionsWith(FloatVectorEncoding.Legacy);
		var original = new FloatVector(new float[] { 0.5f, -1.5f, 2.25f });

		var json = JsonSerializer.Serialize(original, options);
		var roundTripped = JsonSerializer.Deserialize<FloatVector>(json, options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(original.Data.ToArray());
	}

	[Test]
	public void RoundTrip_Base64Encoding_PreservesValues()
	{
		var options = OptionsWith(FloatVectorEncoding.Base64);
		var original = new FloatVector(new float[] { 0.5f, -1.5f, 2.25f, 100f });

		var json = JsonSerializer.Serialize(original, options);
		var roundTripped = JsonSerializer.Deserialize<FloatVector>(json, options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(original.Data.ToArray());
	}

	[Test]
	public void Deserialize_AcceptsLegacyArray_RegardlessOfEncodingSetting()
	{
		var options = OptionsWith(FloatVectorEncoding.Base64);

		var roundTripped = JsonSerializer.Deserialize<FloatVector>("[1.0,2.0,3.0]", options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(new[] { 1f, 2f, 3f });
	}

	[Test]
	public void ImplicitConversion_FromFloatArray_Works()
	{
		FloatVector vec = new float[] { 1f, 2f };

		_ = vec.Length.Should().Be(2);
	}

	[Test]
	public void ImplicitConversion_FromList_Works()
	{
		FloatVector vec = new List<float> { 1f, 2f, 3f };

		_ = vec.Length.Should().Be(3);
	}

	private static JsonSerializerOptions OptionsWith(FloatVectorEncoding encoding)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new ContextProvider<EsqlVectorEncodingContext>(new EsqlVectorEncodingContext(encoding, ByteVectorEncoding.Legacy)));
		return options;
	}
}
