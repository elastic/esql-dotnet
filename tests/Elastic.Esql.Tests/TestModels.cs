// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
/// Document with enum property.
/// </summary>
public class EventDocument
{
	public DateTime Timestamp { get; set; }
	public LogLevel Level { get; set; }
	public string Message { get; set; } = string.Empty;
	public Guid EventId { get; set; }
}
