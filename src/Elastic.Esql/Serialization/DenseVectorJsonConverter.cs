// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Esql.Formatting;

namespace Elastic.Esql.Serialization;

/// <summary>
/// Produces converters for <see cref="DenseVector{T}"/>. Only <c>T = float</c> and <c>T = byte</c>
/// are supported; any other element type throws at construction time.
/// </summary>
public sealed class DenseVectorJsonConverterFactory : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert) =>
		typeToConvert.IsGenericType
		&& typeToConvert.GetGenericTypeDefinition() == typeof(DenseVector<>);

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var elementType = typeToConvert.GetGenericArguments()[0];

		if (elementType == typeof(float))
			return new DenseVectorFloatJsonConverter();

		if (elementType == typeof(byte))
			return new DenseVectorByteJsonConverter();

		throw new NotSupportedException(
			$"DenseVector<{elementType.Name}> is not supported. Only DenseVector<float> and DenseVector<byte> are valid element types.");
	}
}

/// <summary>
/// Reads / writes <see cref="DenseVector{T}"/> with <c>T = float</c> as a JSON array of numbers.
/// Throws <see cref="NotSupportedException"/> on NaN or Infinity values during writing; those values
/// are not representable in ES|QL.
/// </summary>
internal sealed class DenseVectorFloatJsonConverter : JsonConverter<DenseVector<float>>
{
	public override DenseVector<float> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException("Expected start of array for DenseVector<float>.");

		// Vectors are hundreds to a few thousand elements; a rented buffer grows without leaving List<T> arrays behind.
		var scratch = ArrayPool<float>.Shared.Rent(256);
		var count = 0;

		try
		{
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					return new DenseVector<float>(scratch.AsSpan(0, count).ToArray());

				if (count == scratch.Length)
				{
					var grown = ArrayPool<float>.Shared.Rent(scratch.Length * 2);
					scratch.AsSpan(0, count).CopyTo(grown);
					ArrayPool<float>.Shared.Return(scratch);
					scratch = grown;
				}

				scratch[count++] = reader.GetSingle();
			}

			throw new JsonException("Unexpected end of JSON while reading DenseVector<float>.");
		}
		finally
		{
			ArrayPool<float>.Shared.Return(scratch);
		}
	}

	public override void Write(Utf8JsonWriter writer, DenseVector<float> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		var span = value.Span;
		for (var i = 0; i < span.Length; i++)
		{
			var element = span[i];
			if (float.IsNaN(element) || float.IsInfinity(element))
				throw EsqlFormatting.NonFiniteVectorElementNotSupported(i);

			// WriteNumberValue renders whole floats without a decimal point (1.0f -> 1), which ES
			// types as an integer element; keep the explicit literal so the parameter stays float-typed.
			writer.WriteRawValue(EsqlFormatting.FormatFloat(element), skipInputValidation: true);
		}

		writer.WriteEndArray();
	}
}

/// <summary>
/// Reads / writes <see cref="DenseVector{T}"/> with <c>T = byte</c> as a JSON array of byte-valued
/// numbers (ES|QL <c>byte</c> / <c>bit</c> wire format).
/// </summary>
/// <remarks>
/// Writing always uses signed-byte semantics ([-128, 127]) which ES|QL requires on ingest and in
/// query parameters. Reading accepts both signed and unsigned representations: ES|QL responses
/// for dense_vector byte fields can return either form depending on the path, so values in
/// [-128, 255] are mapped to the user's natural unsigned <see cref="byte"/> representation
/// (e.g. <c>-1</c> on the wire and <c>255</c> on the wire both deserialise to byte <c>255</c>).
/// </remarks>
internal sealed class DenseVectorByteJsonConverter : JsonConverter<DenseVector<byte>>
{
	public override DenseVector<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException("Expected start of array for DenseVector<byte>.");

		// Vectors are hundreds to a few thousand elements; a rented buffer grows without leaving List<T> arrays behind.
		var scratch = ArrayPool<byte>.Shared.Rent(256);
		var count = 0;

		try
		{
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.EndArray)
					return new DenseVector<byte>(scratch.AsSpan(0, count).ToArray());

				if (reader.TokenType != JsonTokenType.Number)
					throw new JsonException($"DenseVector<byte> elements must be JSON numbers, got {reader.TokenType}.");

				// Accept both signed (-128..127) and unsigned (0..255) representations. ES|QL
				// responses for dense_vector byte fields can use either form depending on the path.
				if (!reader.TryGetInt32(out var raw))
				{
					var asDouble = reader.GetDouble();
					var rounded = Math.Round(asDouble);
					if (Math.Abs(asDouble - rounded) > double.Epsilon)
						throw new JsonException(
							$"DenseVector<byte> element {asDouble} is not an integer.");
					raw = (int)rounded;
				}

				if (raw is < -128 or > 255)
					throw new JsonException(
						$"DenseVector<byte> element {raw} is outside the supported range [-128, 255].");

				if (count == scratch.Length)
				{
					var grown = ArrayPool<byte>.Shared.Rent(scratch.Length * 2);
					scratch.AsSpan(0, count).CopyTo(grown);
					ArrayPool<byte>.Shared.Return(scratch);
					scratch = grown;
				}

				scratch[count++] = (byte)(raw & 0xFF);
			}

			throw new JsonException("Unexpected end of JSON while reading DenseVector<byte>.");
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(scratch);
		}
	}

	public override void Write(Utf8JsonWriter writer, DenseVector<byte> value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		var span = value.Span;
		for (var i = 0; i < span.Length; i++)
			writer.WriteNumberValue((sbyte)span[i]);

		writer.WriteEndArray();
	}
}
