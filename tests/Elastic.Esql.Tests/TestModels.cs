// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Esql.Tests;

// ============================================================================
// MAPPING CONTEXT: registers all test types for ES|QL tests
// ============================================================================

[ElasticsearchMappingContext]
[Index<LogEntry>(SearchPattern = "logs-*")]
[Index<SimpleDocument>(Name = "simple-docs")]
[Index<MetricDocument>(SearchPattern = "metrics-*")]
[Index<EventDocument>(SearchPattern = "events-*")]
public static partial class EsqlTestMappingContext;

/// <summary>
/// Primary test document type with various field types and attributes.
/// </summary>
[Index(SearchPattern = "logs-*")]
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
[Index(Name = "simple-docs")]
public class SimpleDocument
{
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
	public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Document with nullable properties.
/// </summary>
[Index(SearchPattern = "metrics-*")]
public class MetricDocument
{
	public DateTime Timestamp { get; set; }
	public string Name { get; set; } = string.Empty;
	public double? Value { get; set; }
	public int? Count { get; set; }
	public string? Tags { get; set; }
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
[Index(SearchPattern = "events-*")]
public class EventDocument
{
	public DateTime Timestamp { get; set; }
	public LogLevel Level { get; set; }
	public string Message { get; set; } = string.Empty;
	public Guid EventId { get; set; }
}
