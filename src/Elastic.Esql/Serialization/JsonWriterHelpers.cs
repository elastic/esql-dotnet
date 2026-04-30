// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Esql.Serialization;

internal delegate void JsonWriteFunc<T>(Utf8JsonWriter writer, JsonSerializerOptions options, T value);

internal static class JsonWriterHelpers
{
	/// <summary>
	/// Writes a <see cref="ReadOnlyMemory{T}"/> as a JSON array. When <paramref name="writeElement"/>
	/// is null, falls back to <see cref="JsonSerializerOptions.GetConverter(Type)"/>; the vector
	/// converters always supply a delegate (or use the well-known <c>float</c> converter), so the
	/// trim/AOT-unsafe fallback path is never reached at runtime in this codebase.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "GetConverter fallback only triggers when writeElement is null; vector converters always supply a delegate or use well-known primitive types.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "GetConverter fallback only triggers when writeElement is null; vector converters always supply a delegate or use well-known primitive types.")]
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

				converter.Write(w, v, o);
			};
		}

		writer.WriteStartArray();

		var span = memory.Span;
		foreach (var element in span)
			writeElement(writer, options, element);

		writer.WriteEndArray();
	}
}
