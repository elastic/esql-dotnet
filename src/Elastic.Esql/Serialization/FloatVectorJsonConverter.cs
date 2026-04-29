// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Elastic.Esql.Vectors;

namespace Elastic.Esql.Serialization;

/// <summary>
/// Converts <see cref="FloatVector"/> values to and from either a legacy JSON array of floats
/// or a base64-encoded little-endian <c>float32[]</c> blob (Elasticsearch 9.3.0+). Encoding is
/// resolved from a <see cref="EsqlVectorEncodingContext"/> attached via <see cref="ContextProvider{TContext}"/>.
/// </summary>
public sealed class FloatVectorJsonConverter : JsonConverter<FloatVector>
{
	public override FloatVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Null => default,
			JsonTokenType.StartArray => new FloatVector(reader.ReadCollectionValue<float>(options, null)!.ToArray()),
			JsonTokenType.String => new FloatVector(ReadBase64VectorData(ref reader)),
			_ => throw reader.UnexpectedTokenException(JsonTokenType.StartArray, JsonTokenType.String)
		};

	public override void Write(Utf8JsonWriter writer, FloatVector value, JsonSerializerOptions options)
	{
		var encoding = ContextProvider<EsqlVectorEncodingContext>.TryGetContext(options, out var ctx)
			? ctx.FloatVectorEncoding
			: FloatVectorEncoding.Legacy;

		switch (encoding)
		{
			case FloatVectorEncoding.Legacy:
				writer.WriteMemoryValue(options, value.Data, null);
				break;
			case FloatVectorEncoding.Base64:
				WriteBase64VectorData(writer, value.Data);
				break;
			default:
				throw new NotSupportedException($"Unsupported FloatVectorEncoding: {encoding}");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ReadOnlyMemory<float> ReadBase64VectorData(ref Utf8JsonReader reader)
	{
		var bytes = reader.GetBytesFromBase64();

		if ((bytes.Length & 3) != 0)
			throw new ArgumentException("Decoded vector data length is not a multiple of 4 (not valid 32-bit floats).");

		if (BitConverter.IsLittleEndian)
		{
			var intSpan = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
			for (var i = 0; i < intSpan.Length; i++)
				intSpan[i] = BinaryPrimitives.ReverseEndianness(intSpan[i]);
		}

		var result = new float[bytes.Length / 4];
		Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
		return new ReadOnlyMemory<float>(result);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteBase64VectorData(Utf8JsonWriter writer, ReadOnlyMemory<float> value)
	{
		if (value.IsEmpty)
		{
			writer.WriteStringValue(string.Empty);
			return;
		}

		if (!BitConverter.IsLittleEndian)
		{
			writer.WriteBase64StringValue(MemoryMarshal.AsBytes(value.Span));
			return;
		}

		var pool = MemoryPool<byte>.Shared;
		var required = checked(value.Length * sizeof(float));
		using var owner = pool.Rent(required);

		var dest = owner.Memory.Span[..required];
		var intSource = MemoryMarshal.Cast<float, int>(value.Span);
		var intDest = MemoryMarshal.Cast<byte, int>(dest);

		for (var i = 0; i < intSource.Length; i++)
			intDest[i] = BinaryPrimitives.ReverseEndianness(intSource[i]);

		writer.WriteBase64StringValue(dest);
	}
}
