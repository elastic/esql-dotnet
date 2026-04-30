// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Elastic.Esql.Vectors;

namespace Elastic.Esql.Serialization;

/// <summary>
/// Converts <see cref="ByteVector"/> values to and from a JSON array of signed bytes (legacy),
/// hex string (Elasticsearch 8.14.0+), or base64 string (Elasticsearch 9.3.0+). Encoding is
/// resolved from a <see cref="EsqlVectorEncodingContext"/> attached via <see cref="ContextProvider{TContext}"/>.
/// </summary>
public sealed class ByteVectorJsonConverter : JsonConverter<ByteVector>
{
	public override ByteVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.Null => default,
			JsonTokenType.StartArray => new ByteVector(
				// ES|QL returns dense_vector byte values as signed-semantics JSON floats (e.g. [-1.0, 0.0, 0.0]
				// for [255, 0, 0]). Read via GetDouble + unchecked int + byte cast: handles float-formatted
				// signed output from ES (-1.0 -> 255) and signed/unsigned integer input from clients alike.
				reader.ReadCollectionValue<byte>(options, static (ref r, _) => unchecked((byte)(int)r.GetDouble()))!.ToArray()),
			JsonTokenType.String => ReadStringValue(ref reader, options),
			_ => throw reader.UnexpectedTokenException(JsonTokenType.StartArray, JsonTokenType.String)
		};

	private static ByteVector ReadStringValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
	{
		var encoding = ContextProvider<EsqlVectorEncodingContext>.TryGetContext(options, out var ctx)
			? ctx.ByteVectorEncoding
			: ByteVectorEncoding.Legacy;

		return encoding switch
		{
			ByteVectorEncoding.Hex => new ByteVector(ReadHexVectorData(ref reader)),
			ByteVectorEncoding.Base64 => new ByteVector(reader.GetBytesFromBase64()),
			_ => new ByteVector(ReadStringVectorAutoDetect(ref reader))
		};
	}

	public override void Write(Utf8JsonWriter writer, ByteVector value, JsonSerializerOptions options)
	{
		var encoding = ContextProvider<EsqlVectorEncodingContext>.TryGetContext(options, out var ctx)
			? ctx.ByteVectorEncoding
			: ByteVectorEncoding.Legacy;

		switch (encoding)
		{
			case ByteVectorEncoding.Legacy:
				writer.WriteMemoryValue(options, value.Data, static (w, _, b) => w.WriteNumberValue(unchecked((sbyte)b)));
				break;
			case ByteVectorEncoding.Hex:
				WriteHexVectorData(writer, value.Data);
				break;
			case ByteVectorEncoding.Base64:
				writer.WriteBase64StringValue(value.Data.Span);
				break;
			default:
				throw new NotSupportedException($"Unsupported ByteVectorEncoding: {encoding}");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ReadOnlyMemory<byte> ReadStringVectorAutoDetect(ref Utf8JsonReader reader)
	{
		if (reader.TryGetBytesFromBase64(out var result))
			return result;

		return ReadHexVectorData(ref reader);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static ReadOnlyMemory<byte> ReadHexVectorData(ref Utf8JsonReader reader)
	{
#if NET5_0_OR_GREATER
		var data = Convert.FromHexString(reader.GetString()!);
#else
		var data = FromHex(reader.GetString()!);
#endif
		return new ReadOnlyMemory<byte>(data);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void WriteHexVectorData(Utf8JsonWriter writer, ReadOnlyMemory<byte> value)
	{
		if (value.IsEmpty)
		{
			writer.WriteStringValue(string.Empty);
			return;
		}

		var pool = MemoryPool<char>.Shared;
		var required = checked(value.Length * 2);
		using var owner = pool.Rent(required);

		var source = value.Span;
		var dest = owner.Memory.Span[..required];

		for (int bx = 0, cx = 0; bx < source.Length; ++bx, ++cx)
		{
			var b = (byte)(source[bx] >> 4);
			dest[cx] = (char)(b > 9 ? b + 0x37 : b + 0x30);
			b = (byte)(source[bx] & 0x0F);
			dest[++cx] = (char)(b > 9 ? b + 0x37 : b + 0x30);
		}

		writer.WriteStringValue(dest);
	}

#if !NET5_0_OR_GREATER
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static byte[] FromHex(string data)
	{
		if (data.Length is 0)
			return [];

		if (data.Length % 2 != 0)
			throw new ArgumentException("Decoded vector data length is not a multiple of 2 (not valid 8-bit hex niblets).");

		var buffer = new byte[data.Length / 2];

		for (int bx = 0, sx = 0; bx < buffer.Length; ++bx, ++sx)
		{
			var c = data[sx];
			buffer[bx] = (byte)((c > '9' ? (c > 'Z' ? (c - 'a' + 10) : (c - 'A' + 10)) : (c - '0')) << 4);
			c = data[++sx];
			buffer[bx] |= (byte)(c > '9' ? (c > 'Z' ? (c - 'a' + 10) : (c - 'A' + 10)) : (c - '0'));
		}

		return buffer;
	}
#endif
}
