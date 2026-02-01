// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Examples.Domain.Models;

/// <summary>
/// Application log entry following ECS (Elastic Common Schema).
/// Demonstrates data stream pattern for time-series data.
/// </summary>
[DataStream(Type = "logs", Dataset = "ecommerce.app", Namespace = "production")]
public partial class ApplicationLog
{
	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	[Keyword]
	public LogLevel Level { get; set; }

	[JsonPropertyName("log.logger")]
	[Keyword]
	public string Logger { get; set; } = string.Empty;

	[Text(Analyzer = "standard")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("error.message")]
	[Text]
	public string? ErrorMessage { get; set; }

	[JsonPropertyName("error.stack_trace")]
	[Text(Index = false)]
	public string? StackTrace { get; set; }

	[JsonPropertyName("error.type")]
	[Keyword]
	public string? ErrorType { get; set; }

	[JsonPropertyName("service.name")]
	[Keyword]
	public string ServiceName { get; set; } = string.Empty;

	[JsonPropertyName("service.version")]
	[Keyword]
	public string? ServiceVersion { get; set; }

	[JsonPropertyName("service.environment")]
	[Keyword]
	public string? Environment { get; set; }

	[JsonPropertyName("host.name")]
	[Keyword]
	public string? HostName { get; set; }

	[JsonPropertyName("host.ip")]
	[Ip]
	public string? HostIp { get; set; }

	[JsonPropertyName("trace.id")]
	[Keyword]
	public string? TraceId { get; set; }

	[JsonPropertyName("span.id")]
	[Keyword]
	public string? SpanId { get; set; }

	[JsonPropertyName("transaction.id")]
	[Keyword]
	public string? TransactionId { get; set; }

	[JsonPropertyName("user.id")]
	[Keyword]
	public string? UserId { get; set; }

	[JsonPropertyName("http.request.method")]
	[Keyword]
	public string? HttpMethod { get; set; }

	[JsonPropertyName("url.path")]
	[Keyword]
	public string? UrlPath { get; set; }

	[JsonPropertyName("http.response.status_code")]
	public int? HttpStatusCode { get; set; }

	[JsonPropertyName("event.duration")]
	public long? DurationNanos { get; set; }

	[Object]
	public LogLabels? Labels { get; set; }

	[JsonIgnore]
	public bool Processed { get; set; }
}

public enum LogLevel
{
	Trace,
	Debug,
	Info,
	Warn,
	Error,
	Fatal
}

/// <summary>
/// Custom labels/tags for the log entry.
/// </summary>
public class LogLabels
{
	[Keyword]
	public string? OrderId { get; set; }

	[Keyword]
	public string? ProductId { get; set; }

	[Keyword]
	public string? CustomerId { get; set; }

	[Keyword]
	public string? Action { get; set; }
}
