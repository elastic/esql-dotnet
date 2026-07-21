// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Esql.Tests.Functions.Vector;

public class DenseVectorTests
{
	[Test]
	public void DenseVectorFloat_Serializes_AsJsonNumberArray()
	{
		var vector = new DenseVector<float>([0.5f, 0.25f, 0.75f]);

		var json = JsonSerializer.Serialize(vector);

		_ = json.Should().Be("[0.5,0.25,0.75]");
	}

	[Test]
	public void DenseVectorFloat_Deserializes_FromJsonNumberArray()
	{
		var vector = JsonSerializer.Deserialize<DenseVector<float>>("[0.5, 0.25, 0.75]");

		_ = vector.Length.Should().Be(3);
		_ = vector.ToArray().Should().Equal(0.5f, 0.25f, 0.75f);
	}

	[Test]
	public void DenseVectorByte_Serializes_AsSignedByteJsonArray()
	{
		// Unsigned byte 255 (RGB white) -> signed -1 on the wire.
		var vector = new DenseVector<byte>([255, 0, 128, 100]);

		var json = JsonSerializer.Serialize(vector);

		_ = json.Should().Be("[-1,0,-128,100]");
	}

	[Test]
	public void DenseVectorByte_Deserializes_SignedJsonBackToUnsigned()
	{
		var vector = JsonSerializer.Deserialize<DenseVector<byte>>("[-1, 0, -128, 100]");

		_ = vector.Length.Should().Be(4);
		_ = vector.ToArray().Should().Equal((byte)255, (byte)0, (byte)128, (byte)100);
	}

	[Test]
	public void DenseVectorByte_Deserializes_UnsignedJsonAlsoSupported()
	{
		// ES|QL responses for dense_vector byte fields can use unsigned (0..255) representation.
		var vector = JsonSerializer.Deserialize<DenseVector<byte>>("[255, 0, 128, 100]");

		_ = vector.Length.Should().Be(4);
		_ = vector.ToArray().Should().Equal((byte)255, (byte)0, (byte)128, (byte)100);
	}

	[Test]
	public void DenseVectorByte_Deserializes_OutOfRange_Throws()
	{
		var act = () => JsonSerializer.Deserialize<DenseVector<byte>>("[256]");

		_ = act.Should().Throw<JsonException>().WithMessage("*256*[-128, 255]*");
	}

	[Test]
	public void DenseVectorFloat_NaN_InWrite_Throws()
	{
		var vector = new DenseVector<float>([1.0f, float.NaN, 3.0f]);

		var act = () => JsonSerializer.Serialize(vector);

		_ = act.Should().Throw<JsonException>().WithMessage("*NaN*");
	}

	[Test]
	public void DenseVectorFloat_Infinity_InWrite_Throws()
	{
		var vector = new DenseVector<float>([1.0f, float.PositiveInfinity, 3.0f]);

		var act = () => JsonSerializer.Serialize(vector);

		_ = act.Should().Throw<JsonException>().WithMessage("*Infinity*");
	}

	[Test]
	public void DenseVectorFromInt_RejectsAtConverterCreation()
	{
		var act = () => JsonSerializer.Serialize(new DenseVector<int>([1, 2, 3]));

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*DenseVector*int*");
	}

	[Test]
	public void DenseVectorFloat_ImplicitFromArray_ConstructsFromMemory()
	{
		DenseVector<float> vec = new float[] { 1f, 2f, 3f };

		_ = vec.Length.Should().Be(3);
		_ = vec.ToArray().Should().Equal(1f, 2f, 3f);
	}

	[Test]
	public void DenseVectorFloat_ImplicitFromReadOnlyMemory_ConstructsCorrectly()
	{
		DenseVector<float> vec = new ReadOnlyMemory<float>([1f, 2f, 3f]);

		_ = vec.Length.Should().Be(3);
	}

	[Test]
	public void DenseVectorFloat_WholeValues_SerializeWithExplicitDecimalPoint()
	{
		var vector = new DenseVector<float>([1f, 2f, 3f]);

		var json = JsonSerializer.Serialize(vector);

		_ = json.Should().Be("[1.0,2.0,3.0]");
	}
}
