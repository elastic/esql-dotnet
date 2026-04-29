// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

using Elastic.Esql.Serialization;
using Elastic.Esql.Vectors;

namespace Elastic.Esql.Tests.TypeMapping.Vectors;

public class ByteVectorConverterTests
{
	[Test]
	public void Serialize_LegacyEncoding_WritesSignedBytesArray()
	{
		var options = OptionsWith(ByteVectorEncoding.Legacy);
		var vec = new ByteVector(new byte[] { 0, 1, 2, 255 });

		var json = JsonSerializer.Serialize(vec, options);

		// 255 unsigned -> -1 signed in JSON
		_ = json.Should().Be("[0,1,2,-1]");
	}

	[Test]
	public void Serialize_HexEncoding_WritesHexString()
	{
		var options = OptionsWith(ByteVectorEncoding.Hex);
		var vec = new ByteVector(new byte[] { 0x12, 0xAB, 0xCD });

		var json = JsonSerializer.Serialize(vec, options);

		_ = json.Should().Be("\"12ABCD\"");
	}

	[Test]
	public void Serialize_Base64Encoding_WritesBase64String()
	{
		var options = OptionsWith(ByteVectorEncoding.Base64);
		var vec = new ByteVector(new byte[] { 0x01, 0x02, 0x03 });

		var json = JsonSerializer.Serialize(vec, options);

		_ = json.Should().Be("\"AQID\"");
	}

	[Test]
	public void RoundTrip_LegacyEncoding_PreservesValues()
	{
		var options = OptionsWith(ByteVectorEncoding.Legacy);
		var original = new ByteVector(new byte[] { 0, 127, 128, 255 });

		var json = JsonSerializer.Serialize(original, options);
		var roundTripped = JsonSerializer.Deserialize<ByteVector>(json, options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(original.Data.ToArray());
	}

	[Test]
	public void RoundTrip_HexEncoding_PreservesValues()
	{
		var options = OptionsWith(ByteVectorEncoding.Hex);
		var original = new ByteVector(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });

		var json = JsonSerializer.Serialize(original, options);
		var roundTripped = JsonSerializer.Deserialize<ByteVector>(json, options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(original.Data.ToArray());
	}

	[Test]
	public void RoundTrip_Base64Encoding_PreservesValues()
	{
		var options = OptionsWith(ByteVectorEncoding.Base64);
		var original = new ByteVector(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

		var json = JsonSerializer.Serialize(original, options);
		var roundTripped = JsonSerializer.Deserialize<ByteVector>(json, options);

		_ = roundTripped.Data.ToArray().Should().BeEquivalentTo(original.Data.ToArray());
	}

	[Test]
	public void ImplicitConversion_FromByteArray_Works()
	{
		ByteVector vec = new byte[] { 1, 2, 3 };

		_ = vec.Length.Should().Be(3);
	}

	private static JsonSerializerOptions OptionsWith(ByteVectorEncoding encoding)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new ContextProvider<EsqlVectorEncodingContext>(new EsqlVectorEncodingContext(FloatVectorEncoding.Legacy, encoding)));
		return options;
	}
}
