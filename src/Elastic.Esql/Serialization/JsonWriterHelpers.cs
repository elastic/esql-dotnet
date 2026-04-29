// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Esql.Serialization;

internal delegate void JsonWriteFunc<T>(Utf8JsonWriter writer, JsonSerializerOptions options, T value);

internal static class JsonWriterHelpers
{
	/// <summary>Writes a <see cref="ReadOnlyMemory{T}"/> as a JSON array.</summary>
	public static void WriteMemoryValue<T>(this Utf8JsonWriter writer, JsonSerializerOptions options, ReadOnlyMemory<T> memory, JsonWriteFunc<T>? writeElement)
	{
		if (writeElement is null)
		{
			var converter = (JsonConverter<T>)options.GetConverter(typeof(T));
			writeElement = (w, o, v) =>
			{
				if (v is null && !converter.HandleNull)
				{
					w.WriteNullValue();
					return;
				}

				converter.Write(w, v!, o);
			};
		}

		writer.WriteStartArray();

		var span = memory.Span;
		foreach (var element in span)
			writeElement(writer, options, element);

		writer.WriteEndArray();
	}
}
