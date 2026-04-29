// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Elastic.Esql.Serialization;

/// <summary>
/// Reader-side helpers used by the vector converters. Slim port of selected members from
/// <c>Elastic.Clients.Elasticsearch.Serialization.JsonReaderExtensions</c>.
/// </summary>
internal delegate T? JsonReadFunc<out T>(ref Utf8JsonReader reader, JsonSerializerOptions options);

internal static class JsonReaderHelpers
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ValidateToken(this ref Utf8JsonReader reader, JsonTokenType expected)
	{
		if (reader.TokenType != expected)
			throw new JsonException($"Expected JSON '{expected}' token, but got '{reader.TokenType}'.");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static JsonException UnexpectedTokenException(this ref Utf8JsonReader reader, params ReadOnlySpan<JsonTokenType> expected)
	{
		string valid;
		if (expected.Length <= 1)
		{
			valid = $"'{expected[0]}'";
		}
		else
		{
			valid = string.Join(",", expected[..^1].ToArray().Select(x => $"'{x}'"));
			valid += $" or '{expected[^1]}'";
		}

		return new JsonException($"Expected JSON {valid} token, but got '{reader.TokenType}'.");
	}

	/// <summary>
	/// Reads a JSON array as a <see cref="List{T}"/>.
	/// </summary>
	public static List<T>? ReadCollectionValue<T>(this ref Utf8JsonReader reader, JsonSerializerOptions options, JsonReadFunc<T>? readElement)
	{
		if (reader.TokenType is JsonTokenType.Null)
			return null;

		reader.ValidateToken(JsonTokenType.StartArray);

		readElement ??= static (ref r, o) =>
		{
			var converter = (System.Text.Json.Serialization.JsonConverter<T>)o.GetConverter(typeof(T));
			return converter.Read(ref r, typeof(T), o);
		};

		var result = new List<T>();
		while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
			result.Add(readElement(ref reader, options)!);

		return result;
	}
}
