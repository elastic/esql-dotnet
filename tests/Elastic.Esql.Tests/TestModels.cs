// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests;

/// <summary>
/// Primary test document type with various field types and attributes.
/// </summary>
[EsqlIndex("logs-*")]
public class LogEntry
{
	[EsqlField("@timestamp")]
	public DateTime Timestamp { get; set; }

	[EsqlField("log.level")]
	public string Level { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;  // → "message"

	public int StatusCode { get; set; }  // → "statusCode"

	public double Duration { get; set; }  // → "duration"

	public bool IsError { get; set; }  // → "isError"

	public string? ClientIp { get; set; }  // → "clientIp"

	public string? ServerName { get; set; }  // → "serverName"

	[EsqlIgnore]
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
[EsqlIndex("metrics-*")]
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
[EsqlIndex("events-*")]
public class EventDocument
{
	public DateTime Timestamp { get; set; }
	public LogLevel Level { get; set; }
	public string Message { get; set; } = string.Empty;
	public Guid EventId { get; set; }
}
