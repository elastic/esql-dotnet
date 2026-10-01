// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Serialization;

namespace Elastic.Esql.Tests.Serialization;

public class DenseVectorJsonConverterReadTests
{
	private static readonly JsonSerializerOptions Options = new() { Converters = { new DenseVectorJsonConverterFactory() } };

	[Test]
	public void Read_FloatVectorLargerThanInitialScratch_RoundTrips()
	{
		var values = Enumerable.Range(0, 1000).Select(i => i * 0.5f).ToArray();
		var json = JsonSerializer.Serialize(values);

		var vector = JsonSerializer.Deserialize<DenseVector<float>>(json, Options);

		vector.Span.ToArray().Should().Equal(values);
	}

	[Test]
	public void Read_ByteVectorWithSignedAndUnsignedForms_RoundTrips()
	{
		var vector = JsonSerializer.Deserialize<DenseVector<byte>>("[-1,255,0,127,-128]", Options);

		vector.Span.ToArray().Should().Equal(255, 255, 0, 127, 128);
	}

	[Test]
	public void Read_TruncatedArray_Throws()
	{
		var act = () => JsonSerializer.Deserialize<DenseVector<float>>("[1.0,2.0", Options);

		act.Should().Throw<JsonException>();
	}
}
