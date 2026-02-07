// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// =============================================================================
// ELASTIC.ESQL & ELASTIC.MAPPING PLAYGROUND
// =============================================================================
// This demo showcases the key features of the Elastic.Esql and Elastic.Mapping
// libraries using a realistic "IT Support Ticket System" use case.
//
// Features demonstrated:
// 1. DTO mapping with nested objects and ECS-style field names
// 2. Code-based configuration of analyzers, tokenizers, and filters
// 3. Type-safe mappings builder with runtime fields and dynamic templates
// 4. Automatic index template bootstrapping via Elastic.Ingest channels
// 5. Strongly-typed ES|QL queries with LINQ translation
// =============================================================================

using Elastic.Clients.Elasticsearch;
using Elastic.Esql;
using Elastic.Examples.Ingest.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Transport;
using Playground.Helpers;
using Playground.Models;
using static Playground.Helpers.ConnectionHelper;
using static Playground.Helpers.QueryRunner;

// =============================================================================
// CONFIGURATION
// =============================================================================

var (url, apiKey) = ElasticsearchConfiguration.Get();
Console.WriteLine($"Elasticsearch: {url}\n");

// Create transport for raw API calls and ES|QL queries
var transport = new DistributedTransport(new TransportConfiguration(new Uri(url), new ApiKey(apiKey)));

// Create Elasticsearch client for ingestion and management (uses same transport)
var clientSettings = new ElasticsearchClientSettings(new Uri(url))
	.Authentication(new ApiKey(apiKey));
var client = new ElasticsearchClient(clientSettings);

// Create ES|QL client for queries
var esqlSettings = new EsqlClientSettings(transport);
using var esqlClient = new EsqlClient(esqlSettings);

// Verify connection using raw transport
if (!await VerifyConnectionAsync(transport))
	return 1;

// =============================================================================
// CLEANUP & BOOTSTRAP
// =============================================================================

Console.WriteLine("=== Cleaning up existing data ===");
await CleanupAsync();

Console.WriteLine("\n=== Ingesting sample tickets via MappingIndexChannel ===");
var tickets = SampleDataGenerator.GenerateTickets(100);
await IngestViaChannelAsync(tickets);

// =============================================================================
// ES|QL QUERIES SHOWCASE
// =============================================================================

Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("ES|QL QUERY SHOWCASE");
Console.WriteLine(new string('=', 80));

// --- BASIC QUERIES ---
Console.WriteLine("\n--- Basic Filtering & Sorting ---\n");

// Note: Enum value 0 (Open/Low) may serialize as null with default settings
await RunAsync("Pending high-priority tickets",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.Status == TicketStatus.Pending && t.Priority == TicketPriority.High)
		.OrderByDescending(t => t.CreatedAt)
		.Take(5)
);

await RunAsync("All critical tickets (P1)",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.Priority == TicketPriority.Critical)
		.Take(10)
);

await RunAsync("Tickets mentioning 'Email' in subject",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.Subject.Contains("Email"))
		.Take(5)
);

// --- AGGREGATIONS ---
Console.WriteLine("\n--- Aggregations & Statistics ---\n");

await RunAsync("Ticket count by status",
	esqlClient.Query<SupportTicket>()
		.GroupBy(t => t.Status)
		.Select(g => new { Status = g.Key, Count = g.Count() })
);

await RunAsync("Resolved tickets count by priority",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.Status == TicketStatus.Resolved)
		.GroupBy(t => t.Priority)
		.Select(g => new { Priority = g.Key, TicketCount = g.Count() })
);

await RunAsync("Tickets per assignee (top 5)",
	esqlClient.Query<SupportTicket>()
		.GroupBy(t => t.AssignedTo)
		.Select(g => new { Assignee = g.Key, TotalTickets = g.Count() })
		.OrderByDescending(x => x.TotalTickets)
		.Take(5)
);

// --- DATE/TIME OPERATIONS ---
Console.WriteLine("\n--- Date/Time Operations ---\n");

await RunAsync("Tickets created in last 7 days",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.CreatedAt > DateTime.UtcNow.AddDays(-7))
		.OrderByDescending(t => t.CreatedAt)
		.Take(5)
);

await RunAsync("Total ticket count",
	esqlClient.Query<SupportTicket>()
		.GroupBy(t => 1)
		.Select(g => new { Total = g.Count() })
);

// --- COMPLEX FILTERS ---
Console.WriteLine("\n--- Complex Filters ---\n");

await RunAsync("Escalated OR pending tickets",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.IsEscalated || t.Status == TicketStatus.Pending)
		.OrderByDescending(t => t.Priority)
		.Take(5)
);

await RunAsync("Network-related tickets",
	esqlClient.Query<SupportTicket>()
		.Where(t => t.Category == "Network")
		.Take(5)
);

// --- SORTING ---
Console.WriteLine("\n--- Sorting ---\n");

await RunAsync("First 5 tickets by ID",
	esqlClient.Query<SupportTicket>()
		.OrderBy(t => t.TicketId)
		.Take(5)
);

// --- LINQ QUERY SYNTAX ---
// ES|QL also supports LINQ query comprehension syntax (from...where...select)
// as an alternative to method chaining syntax
Console.WriteLine("\n--- LINQ Query Syntax ---\n");

await RunAsync("Critical tickets (query syntax)",
	from ticket in esqlClient.Query<SupportTicket>()
	where ticket.Priority == TicketPriority.Critical
	orderby ticket.CreatedAt descending
	select ticket
);

await RunAsync("Escalated tickets by category (query syntax)",
	from t in esqlClient.Query<SupportTicket>()
	where t.IsEscalated
	orderby t.Category, t.Priority descending
	select new { t.TicketId, t.Category, t.Priority, t.Subject }
);

await RunAsync("Ticket summary by status (query syntax)",
	from t in esqlClient.Query<SupportTicket>()
	group t by t.Status into g
	select new { Status = g.Key, Count = g.Count() }
);

await RunAsync("High priority pending tickets (query syntax)",
	from t in esqlClient.Query<SupportTicket>()
	where t.Status == TicketStatus.Pending
	where t.Priority >= TicketPriority.High
	orderby t.Priority descending, t.CreatedAt
	select new { t.TicketId, t.Subject, t.Priority, t.AssignedTo }
);

Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("Demo complete!");
Console.WriteLine(new string('=', 80));

return 0;

// =============================================================================
// LOCAL HELPER METHODS
// =============================================================================

async Task CleanupAsync()
{
	var indexName = PlaygroundMappingContext.SupportTicket.Context.IndexStrategy?.WriteTarget ?? "support-tickets";

	Console.WriteLine($"  Deleting index '{indexName}'...");
	var delIdx = await client.Indices.DeleteAsync(indexName);
	Console.WriteLine(delIdx.IsValidResponse ? "  OK" : "  skipped");

	Console.WriteLine($"  Deleting index template '{indexName}'...");
	var delTpl = await client.Indices.DeleteIndexTemplateAsync(indexName);
	Console.WriteLine(delTpl.IsValidResponse ? "  OK" : "  skipped");

	Console.WriteLine($"  Deleting component template '{indexName}-write'...");
	var delComp = await client.Cluster.DeleteComponentTemplateAsync($"{indexName}-write");
	Console.WriteLine(delComp.IsValidResponse ? "  OK" : "  skipped");
}

async Task IngestViaChannelAsync(List<SupportTicket> ticketList)
{
	// =========================================================================
	// HOW CONFIGURE* METHODS ARE AUTOMATICALLY INVOKED
	// =========================================================================
	// The source generator populates delegate references in ElasticsearchTypeContext
	// for any Configure* methods defined on your model type T:
	//
	// 1. ConfigureAnalysis delegate - Called during bootstrap to merge custom
	//    analyzers, tokenizers, and filters into the component template's
	//    settings. Accessed via Context.ConfigureAnalysis (no reflection needed).
	//
	// 2. ConfigureMappings - Called by the source generator when building the
	//    mappings JSON. The generated ElasticsearchContext includes your
	//    customizations (runtime fields, multi-fields, etc.)
	//
	// The Context property (PlaygroundMappingContext.SupportTicket.Context) is source-generated and
	// contains pre-computed JSON for settings and mappings, the hash for
	// change detection, and delegates for runtime configuration.
	//
	// Benefits of this approach:
	//   - AOT-compatible (no reflection)
	//   - Compile-time type safety
	//   - Zero runtime overhead
	//
	// See SupportTicket.cs for examples of both Configure* methods.
	// =========================================================================

	var targetIndex = PlaygroundMappingContext.SupportTicket.Context.IndexStrategy?.WriteTarget ?? "support-tickets";
	Console.WriteLine($"  Index target: {targetIndex}");

	var options = new MappingIndexChannelOptions<SupportTicket>(client)
	{
		Context = PlaygroundMappingContext.SupportTicket.Context,
		IndexFormat = targetIndex, // Explicitly set the fixed index name
		BulkOperationIdLookup = t => t.TicketId,
		OnBootstrapStatus = msg => Console.WriteLine($"  {msg}")
	};

	// Create the channel - this automatically configures the index format
	// from Context.IndexStrategy.WriteTarget ("support-tickets")
	var channel = new MappingIndexChannel<SupportTicket>(options);

	try
	{
		// Bootstrap creates component template (with analysis + mappings) and index template
		// ConfigureAnalysis is invoked via the delegate in Context.ConfigureAnalysis
		Console.WriteLine("  Bootstrapping index templates...");
		_ = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		// Ensure index exists (triggers template application)
		var indexName = PlaygroundMappingContext.SupportTicket.Context.IndexStrategy?.WriteTarget ?? "support-tickets";
		Console.WriteLine($"  Ensuring index '{indexName}' exists...");
		var existsResponse = await client.Indices.ExistsAsync(indexName);
		if (!existsResponse.Exists)
		{
			var createResponse = await client.Indices.CreateAsync(indexName);
			if (!createResponse.IsValidResponse)
				Console.WriteLine($"  Warning: {createResponse.ElasticsearchServerError?.Error?.Reason}");
		}

		// Write documents through the channel
		Console.WriteLine($"  Writing {ticketList.Count} tickets...");
		var written = 0;
		foreach (var ticket in ticketList)
		{
			if (channel.TryWrite(ticket))
				written++;
		}

		// Wait for all documents to be indexed
		_ = await channel.WaitForDrainAsync();

		// Refresh to make documents searchable
		_ = await channel.RefreshAsync();

		Console.WriteLine($"  Indexed {written} tickets successfully!");
	}
	finally
	{
		_ = channel.TryComplete();
	}
}
