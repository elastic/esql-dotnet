// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// =============================================================================
// DOMAIN MODEL: SUPPORT TICKET SYSTEM
// =============================================================================
// This demonstrates:
// - Partial class with source-generated ElasticsearchContext
// - ECS-style dotted field names via [JsonPropertyName]
// - Nested objects (Responses, Metadata)
// - Enum serialization with [JsonConverter]
// - Custom analyzers via ConfigureAnalysis
// - Custom mappings via ConfigureMappings with runtime fields
// =============================================================================

using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Playground.Models;

/// <summary>
/// A support ticket in the IT helpdesk system.
/// Demonstrates comprehensive Elastic.Mapping features.
/// </summary>
[Index(Name = "support-tickets", SearchPattern = "support-tickets*")]
public partial class SupportTicket
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

	// =========================================================================
	// ANALYSIS CONFIGURATION
	// =========================================================================
	// Define custom analyzers, tokenizers, and filters.
	// These are automatically included in the index template settings.
	// The source generator creates type-safe accessors: Analysis.Analyzers.TicketContentAnalyzer
	// =========================================================================

	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
		// Custom tokenizer for ticket content - splits on common delimiters
		.Tokenizer("ticket_tokenizer", t => t
			.Pattern()
			.PatternValue(@"[\s\-_/\\.,;:]+"))

		// Word delimiter filter for camelCase and technical terms
		.TokenFilter("ticket_word_filter", f => f
			.WordDelimiterGraph()
			.PreserveOriginal(true)
			.SplitOnCaseChange(true)
			.SplitOnNumerics(true))

		// Main analyzer combining all components
		// Note: synonym_graph must not follow word_delimiter_graph, so we use a simpler chain
		.Analyzer("ticket_content_analyzer", a => a
			.Custom()
			.Tokenizer(Analysis.Tokenizers.TicketTokenizer)
			.Filters(
				TokenFilters.Lowercase,
				Analysis.TokenFilters.TicketWordFilter
			));

	// =========================================================================
	// MAPPINGS CONFIGURATION
	// =========================================================================
	// Customize field mappings, add runtime fields, and dynamic templates.
	// The source generator creates a type-safe builder: SupportTicketMappingsBuilder
	// =========================================================================

	public static SupportTicketMappingsBuilder ConfigureMappings(SupportTicketMappingsBuilder mappings) => mappings
		// Customize Subject field with a keyword multi-field for exact matching
		.Subject(f => f
			.Analyzer(mappings.Analysis.Analyzers.TicketContentAnalyzer)
			.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)))

		// Runtime field: is_overdue (tickets open > 24 hours)
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

		// Runtime field: resolution_time_hours (for resolved tickets)
		.AddRuntimeField("resolution_time_hours", r => r
			.Double()
			.Script("""
				if (doc['ticket.resolved_at'].size() > 0 && doc['@timestamp'].size() > 0) {
					long resolved = doc['ticket.resolved_at'].value.toInstant().toEpochMilli();
					long created = doc['@timestamp'].value.toInstant().toEpochMilli();
					emit((resolved - created) / (1000.0 * 60 * 60));
				}
				"""))

		// Runtime field: priority_label (human-readable priority)
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

		// Dynamic template: map all unknown string fields in 'custom.*' as keywords
		.AddDynamicTemplate("custom_fields_as_keyword", dt => dt
			.PathMatch("custom.*")
			.Mapping(m => m.Keyword()));
}

// =============================================================================
// NESTED OBJECTS
// =============================================================================

/// <summary>
/// Metadata about how the ticket was submitted.
/// Demonstrates [Object] mapping for flat JSON structure.
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
/// Demonstrates [Nested] mapping for array of objects with independent querying.
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

// =============================================================================
// ENUMS
// =============================================================================

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
