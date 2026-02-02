// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

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
	[JsonConverter(typeof(JsonStringEnumConverter))]
	[Keyword]
	public LogLevel Level { get; set; }

	[JsonPropertyName("log.logger")]
	[Keyword]
	public string Logger { get; set; } = string.Empty;

	[Text(Analyzer = "log_message_analyzer")]
	public string Message { get; set; } = string.Empty;

	[JsonPropertyName("error.message")]
	[Text(Analyzer = "error_message_analyzer")]
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

	/// <summary>Configures ApplicationLog-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
		.Tokenizer("log_tokenizer", t => t
			.Pattern()
			.PatternValue("[\\s\\[\\]{}(),;:=|/\\\\]+"))
		.TokenFilter("log_word_delimiter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true)
			.SplitOnNumerics(true))
		.TokenFilter("exception_word_delimiter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true))
		.Analyzer("log_message_analyzer", a => a
			.Custom()
			.Tokenizer(Analysis.Tokenizers.LogTokenizer)
			.Filters(TokenFilters.Lowercase, Analysis.TokenFilters.LogWordDelimiter))
		.Analyzer("error_message_analyzer", a => a
			.Custom()
			.Tokenizer(Tokenizers.Standard)
			.Filters(TokenFilters.Lowercase, Analysis.TokenFilters.ExceptionWordDelimiter));

	/// <summary>Configures ApplicationLog-specific mapping overrides.</summary>
	public static ApplicationLogMappingsBuilder ConfigureMappings(ApplicationLogMappingsBuilder mappings) => mappings
		.Message(f => f
			.Analyzer(mappings.Analysis.Analyzers.LogMessageAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(2048)))
		.ErrorMessage(f => f
			.Analyzer(mappings.Analysis.Analyzers.ErrorMessageAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(1024)))
		.Level(f => f)
		.AddRuntimeField("is_error", r => r
			.Boolean()
			.Script("emit(doc['log.level'].value == 'Error' || doc['log.level'].value == 'Fatal')"))
		.AddRuntimeField("response_time_ms", r => r
			.Double()
			.Script("if (doc['event.duration'].size() > 0) emit(doc['event.duration'].value / 1000000.0)"))
		.AddRuntimeField("hour_of_day", r => r
			.Long()
			.Script("emit(doc['@timestamp'].value.getHour())"))
		.AddDynamicTemplate("labels_as_keyword", dt => dt
			.PathMatch("labels.*")
			.Mapping(m => m.Keyword()));
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
