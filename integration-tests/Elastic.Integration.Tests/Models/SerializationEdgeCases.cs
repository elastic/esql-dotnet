// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

/// <summary>
/// A type with a top-level JsonConverter. ES|QL cannot decompose this into columns
/// and should reject it with NotSupportedException.
/// </summary>
[JsonConverter(typeof(TopLevelDocumentConverter))]
public class TypeWithTopLevelConverter
{
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
}

public class TopLevelDocumentConverter : JsonConverter<TypeWithTopLevelConverter>
{
	public override TypeWithTopLevelConverter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var doc = new TypeWithTopLevelConverter();
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;

			var prop = reader.GetString();
			reader.Read();
			if (prop == "name")
				doc.Name = reader.GetString() ?? string.Empty;
			else if (prop == "value")
				doc.Value = reader.GetInt32();
		}
		return doc;
	}

	public override void Write(Utf8JsonWriter writer, TypeWithTopLevelConverter value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteString("name", value.Name);
		writer.WriteNumber("value", value.Value);
		writer.WriteEndObject();
	}
}

/// <summary>
/// A type with a property-level JsonConverter. The converter transforms between
/// a prefixed string ("ID-42") and an integer (42).
/// </summary>
public class TypeWithPropertyConverter
{
	[JsonConverter(typeof(PrefixedIntConverter))]
	public int CustomId { get; set; }

	public string Name { get; set; } = string.Empty;

	public double Value { get; set; }
}

public class PrefixedIntConverter : JsonConverter<int>
{
	public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var s = reader.GetString() ?? throw new JsonException("Expected a string.");
		return int.Parse(s.Replace("ID-", ""), CultureInfo.InvariantCulture);
	}

	public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
		writer.WriteStringValue($"ID-{value}");
}
