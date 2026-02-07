// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;

namespace Elastic.Mapping.Tests;

// ============================================================================
// MAPPING CONTEXT: registers all test types
// ============================================================================

[ElasticsearchMappingContext]
[Index<LogEntry>(
	WriteAlias = "logs-write",
	ReadAlias = "logs-read",
	SearchPattern = "logs-*",
	Shards = 3,
	Replicas = 2
)]
[DataStream<NginxAccessLog>(Type = "logs", Dataset = "nginx.access", Namespace = "production")]
[Index<SimpleDocument>(Name = "simple-docs")]
[Index<AdvancedDocument>(Name = "advanced-docs")]
public static partial class TestMappingContext
{
	/// <summary>Configures LogEntry-specific analysis settings (context-level).</summary>
	public static AnalysisBuilder ConfigureLogEntryAnalysis(AnalysisBuilder analysis) => analysis
		.Analyzer("log_message_analyzer", a => a
			.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase))
		.Normalizer("lowercase", n => n
			.Custom()
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase));
}

// ============================================================================
// DOMAIN TYPES: clean POCOs, no mapping attributes on the class itself
// ============================================================================

/// <summary>
/// Test model with Index configuration (registered via context).
/// </summary>
public class LogEntry
{
	[JsonPropertyName("@timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	[Keyword(Normalizer = "lowercase")]
	public string Level { get; set; } = string.Empty;

	[Text(Analyzer = "standard", Norms = false)]
	public string Message { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	public double Duration { get; set; }

	public bool IsError { get; set; }

	[Ip]
	public string? ClientIp { get; set; }

	[JsonIgnore]
	public string InternalId { get; set; } = string.Empty;
}

/// <summary>
/// Test model with DataStream configuration (registered via context).
/// </summary>
public class NginxAccessLog
{
	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	public DateTime Timestamp { get; set; }

	[Text(Analyzer = "standard")]
	public string Path { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	[Ip]
	public string? ClientIp { get; set; }
}

/// <summary>
/// Test model with minimal configuration.
/// </summary>
public class SimpleDocument
{
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
	public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Test model with advanced field types.
/// </summary>
public class AdvancedDocument
{
	public string Title { get; set; } = string.Empty;

	[GeoPoint]
	public object? Location { get; set; }

	[DenseVector(Dims = 384, Similarity = "cosine")]
	public float[]? Embedding { get; set; }

	[SemanticText(InferenceId = "my-elser-endpoint")]
	public string? SemanticContent { get; set; }

	[Completion(Analyzer = "simple")]
	public string? Suggest { get; set; }

	[Nested]
	public List<Tag>? Tags { get; set; }
}

/// <summary>
/// Nested type for testing.
/// </summary>
public class Tag
{
	public string Name { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
}
