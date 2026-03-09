// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Esql.Tests;

// ============================================================================
// MAPPING CONTEXT: registers all test types for ES|QL tests
// ============================================================================

[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(SimpleDocument))]
[JsonSerializable(typeof(MetricDocument))]
[JsonSerializable(typeof(EventDocument))]
[JsonSerializable(typeof(LanguageLookup))]
[JsonSerializable(typeof(ThreatListEntry))]
[JsonSerializable(typeof(OverlappingLookup))]
[JsonSerializable(typeof(LogProjection))]
[JsonSerializable(typeof(StatsProjection))]
[JsonSerializable(typeof(OrdinalEnumDocument))]
[JsonSerializable(typeof(CustomConverterDocument))]
[JsonSerializable(typeof(RecordProjection))]
[JsonSerializable(typeof(UnmatchedCtorProjection))]
[JsonSerializable(typeof(CollisionRecord))]
public sealed partial class EsqlTestMappingContext : JsonSerializerContext;

/// <summary>
/// Primary test document type with various field types and attributes.
/// </summary>
public class LogEntry
{
	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;  // → "message"

	public int StatusCode { get; set; }  // → "statusCode"

	public double Duration { get; set; }  // → "duration"

	public bool IsError { get; set; }  // → "isError"

	public string? ClientIp { get; set; }  // → "clientIp"

	public string? ServerName { get; set; }  // → "serverName"

	[JsonIgnore]
	public string InternalId { get; set; } = string.Empty;
}

/// <summary>
/// Simple document type without attributes for default naming tests.
/// </summary>
public class SimpleDocument
{
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
	public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Document with nullable properties.
/// </summary>
public class MetricDocument
{
	public DateTime Timestamp { get; set; }
	public string Name { get; set; } = string.Empty;
	public double? Value { get; set; }
	public int? Count { get; set; }
	public string? Tags { get; set; }
}

/// <summary>
/// Lookup document for LOOKUP JOIN tests.
/// </summary>
public class LanguageLookup
{
	public int LanguageCode { get; set; }
	public string LanguageName { get; set; } = string.Empty;
}

/// <summary>
/// Lookup document for IP threat correlation LOOKUP JOIN tests.
/// </summary>
public class ThreatListEntry
{
	public string ClientIp { get; set; } = string.Empty;
	public string ThreatLevel { get; set; } = string.Empty;
}

/// <summary>
/// Lookup document that shares field names with <see cref="LogEntry"/> for join collision tests.
/// Fields <c>Message</c> and <c>ClientIp</c> collide with the outer type.
/// </summary>
public class OverlappingLookup
{
	public string ClientIp { get; set; } = string.Empty;
	public string Message { get; set; } = string.Empty;
	public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Enum for testing enum formatting.
/// </summary>
public enum LogLevel
{
	Debug,
	Info,
	Warning,
	Error,
	Critical
}

/// <summary>
/// Document with enum property using string serialization.
/// </summary>
public class EventDocument
{
	public DateTime Timestamp { get; set; }

	[JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
	public LogLevel Level { get; set; }

	public string Message { get; set; } = string.Empty;
	public Guid EventId { get; set; }
}

/// <summary>
/// Strongly-typed projection model with custom JSON field names for testing
/// that <see cref="JsonPropertyNameAttribute"/> is honored on target types.
/// </summary>
public class LogProjection
{
	[JsonPropertyName("log_level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("status")]
	public int StatusCode { get; set; }

	public double Duration { get; set; }
}

/// <summary>
/// Strongly-typed stats result model for testing GroupBy projections
/// with <see cref="JsonPropertyNameAttribute"/> on result fields.
/// </summary>
public class StatsProjection
{
	[JsonPropertyName("log_level")]
	public string Level { get; set; } = string.Empty;

	[JsonPropertyName("total_count")]
	public int Count { get; set; }

	[JsonPropertyName("avg_duration")]
	public double AvgDuration { get; set; }

	[JsonPropertyName("total_duration")]
	public double TotalDuration { get; set; }
}

/// <summary>
/// Enum for testing ordinal (integer) enum serialization.
/// </summary>
public enum Priority
{
	Low,
	Medium,
	High,
	Critical
}

/// <summary>
/// Document with an enum property that uses default ordinal serialization (no <see cref="JsonStringEnumConverter"/>).
/// </summary>
public class OrdinalEnumDocument
{
	public Priority Priority { get; set; }
	public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Custom converter that serializes an <see cref="int"/> as a prefixed string.
/// </summary>
public class PrefixedIntConverter : JsonConverter<int>
{
	public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var s = reader.GetString() ?? throw new JsonException("Expected a string.");
		return int.Parse(s.Replace("ID-", ""), System.Globalization.CultureInfo.InvariantCulture);
	}

	public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
		writer.WriteStringValue($"ID-{value}");
}

/// <summary>
/// Document with a custom converter on a property, validating that user-provided converters are respected.
/// </summary>
public class CustomConverterDocument
{
	[JsonConverter(typeof(PrefixedIntConverter))]
	public int CustomId { get; set; }

	public string Name { get; set; } = string.Empty;
}

/// <summary>Record projection for testing constructor-call Select.</summary>
public record RecordProjection(string Message, int StatusCode);

/// <summary>
/// Projection whose constructor parameter name does not match any property,
/// used to verify the translator throws. Cannot use a primary constructor here
/// because that would create a matching property for the parameter.
/// </summary>
#pragma warning disable IDE0290
public class UnmatchedCtorProjection
{
	public UnmatchedCtorProjection(string noSuchField) => Name = noSuchField;

	public string Name { get; }
}
#pragma warning restore IDE0290

/// <summary>Record projection for join collision tests with constructor-call syntax.</summary>
public record CollisionRecord(string OuterMsg, string InnerMsg);
