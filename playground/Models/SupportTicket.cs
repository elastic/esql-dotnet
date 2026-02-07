// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Playground.Models;

/// <summary>
/// Elasticsearch mapping context for the playground.
/// </summary>
[ElasticsearchMappingContext]
[Index<SupportTicket>(Name = "support-tickets", SearchPattern = "support-tickets*")]
public static partial class PlaygroundMappingContext
{
	/// <summary>Configures SupportTicket-specific analysis settings.</summary>
	public static AnalysisBuilder ConfigureSupportTicketAnalysis(AnalysisBuilder analysis) => analysis
		.Tokenizer("ticket_tokenizer", t => t
			.Pattern()
			.PatternValue(@"[\s\-_/\\.,;:]+"))
		.TokenFilter("ticket_word_filter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true)
			.SplitOnNumerics(true))
		.Analyzer("ticket_content_analyzer", a => a
			.Custom()
			.Tokenizer(SupportTicketAnalysis.Tokenizers.TicketTokenizer)
			.Filters(
				TokenFilters.Lowercase,
				SupportTicketAnalysis.TokenFilters.TicketWordFilter
			));
}

/// <summary>
/// A support ticket in the IT helpdesk system.
/// Demonstrates mixed strategies: analysis on context, mappings via IConfigureElasticsearch.
/// </summary>
public class SupportTicket : IConfigureElasticsearch<SupportTicketMappingsBuilder>
{
	// --- Core identification ---

	[JsonPropertyName("ticket.id")]
	[Keyword]
	public string TicketId { get; set; } = string.Empty;

	// --- Ticket content (uses custom analyzers) ---

	[JsonPropertyName("subject")]
	[Text(Analyzer = "ticket_content_analyzer")]
	public string Subject { get; set; } = string.Empty;

	[JsonPropertyName("description")]
	[Text(Analyzer = "ticket_content_analyzer")]
	public string Description { get; set; } = string.Empty;

	// --- Status & Priority (enums serialized as strings) ---

	[JsonPropertyName("ticket.status")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	[Keyword]
	public TicketStatus Status { get; set; }

	[JsonPropertyName("ticket.priority")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	[Keyword]
	public TicketPriority Priority { get; set; }

	// --- Classification ---

	[JsonPropertyName("category")]
	[Keyword]
	public string Category { get; set; } = string.Empty;

	[JsonPropertyName("tags")]
	[Keyword]
	public List<string> Tags { get; set; } = [];

	// --- Timestamps ---

	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("ticket.updated_at")]
	[Date]
	public DateTime UpdatedAt { get; set; }

	[JsonPropertyName("ticket.resolved_at")]
	[Date]
	public DateTime? ResolvedAt { get; set; }

	// --- People ---

	[JsonPropertyName("user.email")]
	[Keyword]
	public string ReportedBy { get; set; } = string.Empty;

	[JsonPropertyName("agent.name")]
	[Keyword]
	public string? AssignedTo { get; set; }

	// --- Flags ---

	[JsonPropertyName("ticket.escalated")]
	public bool IsEscalated { get; set; }

	// --- Computed field for queries (stored as null, computed at query time) ---

	[JsonIgnore]
	public double ResolutionTimeMinutes =>
		ResolvedAt.HasValue ? (ResolvedAt.Value - CreatedAt).TotalMinutes : 0;

	// --- Nested objects ---

	[JsonPropertyName("metadata")]
	[Object]
	public TicketMetadata? Metadata { get; set; }

	[JsonPropertyName("responses")]
	[Nested]
	public List<TicketResponse> Responses { get; set; } = [];

	/// <summary>Configures SupportTicket-specific mapping overrides via IConfigureElasticsearch.</summary>
	public static SupportTicketMappingsBuilder ConfigureMappings(SupportTicketMappingsBuilder mappings) => mappings
		.Subject(f => f
			.Analyzer(mappings.Analysis.Analyzers.TicketContentAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)))
		.AddRuntimeField("is_overdue", r => r
			.Boolean()
			.Script("""
				if (doc['ticket.status'].size() > 0 && doc['ticket.status'].value == 'Open') {
					long created = doc['@timestamp'].value.toInstant().toEpochMilli();
					long now = System.currentTimeMillis();
					long hoursDiff = (now - created) / (1000 * 60 * 60);
					emit(hoursDiff > 24);
				} else {
					emit(false);
				}
				"""))
		.AddRuntimeField("resolution_time_hours", r => r
			.Double()
			.Script("""
				if (doc['ticket.resolved_at'].size() > 0 && doc['@timestamp'].size() > 0) {
					long resolved = doc['ticket.resolved_at'].value.toInstant().toEpochMilli();
					long created = doc['@timestamp'].value.toInstant().toEpochMilli();
					emit((resolved - created) / (1000.0 * 60 * 60));
				}
				"""))
		.AddRuntimeField("priority_label", r => r
			.Keyword()
			.Script("""
				if (doc['ticket.priority'].size() > 0) {
					String p = doc['ticket.priority'].value;
					if (p == 'Critical') emit('P1 - Critical');
					else if (p == 'High') emit('P2 - High');
					else if (p == 'Medium') emit('P3 - Medium');
					else emit('P4 - Low');
				}
				"""))
		.AddDynamicTemplate("custom_fields_as_keyword", dt => dt
			.PathMatch("custom.*")
			.Mapping(m => m.Keyword()));
}

/// <summary>
/// Metadata about how the ticket was submitted.
/// </summary>
public class TicketMetadata
{
	[JsonPropertyName("source")]
	[Keyword]
	public string Source { get; set; } = string.Empty;

	[JsonPropertyName("browser")]
	[Keyword]
	public string? Browser { get; set; }

	[JsonPropertyName("os")]
	[Keyword]
	public string? OperatingSystem { get; set; }
}

/// <summary>
/// A response/comment on a ticket.
/// </summary>
public class TicketResponse
{
	[JsonPropertyName("response.id")]
	[Keyword]
	public string ResponseId { get; set; } = string.Empty;

	[JsonPropertyName("author")]
	[Keyword]
	public string Author { get; set; } = string.Empty;

	[JsonPropertyName("content")]
	[Text]
	public string Content { get; set; } = string.Empty;

	[JsonPropertyName("response.created_at")]
	[Date]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("response.internal")]
	public bool IsInternal { get; set; }
}

public enum TicketStatus
{
	Open,
	InProgress,
	Pending,
	Resolved
}

public enum TicketPriority
{
	Low,
	Medium,
	High,
	Critical
}
