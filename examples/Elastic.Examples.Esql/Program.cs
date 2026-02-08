// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Esql;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using Elastic.Examples.Domain;
using Elastic.Examples.Domain.Models;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using XenoAtom.Terminal;

var jsonOptions = new JsonSerializerOptions
{
	WriteIndented = false,
	PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	Converters = { new JsonStringEnumConverter() }
};

Terminal.Title = "Elastic.Examples.Esql";
WriteOutput("[bold]Elastic.Examples.Esql[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n\n");

// Get Elasticsearch connection configuration
var (url, apiKey) = GetElasticsearchConfiguration();

WriteOutput($"[cyan]Elasticsearch:[/] {url}\n\n");

// Create transport with authentication
var transport = CreateTransport(url, apiKey);
var settings = new EsqlClientSettings(transport)
{
	MappingContext = ExampleElasticsearchContext.Instance
};
using var client = new EsqlClient(settings);

// Verify connection
WriteOutput("[cyan]Verifying connection...[/]\n");
try
{
	var testResponse = await client.QueryAsync<dynamic>("ROW message = \"Connected!\"");
	WriteOutput("[bold green]Connected successfully![/]\n\n");
}
catch (EsqlExecutionException ex)
{
	WriteOutput($"[bold red]Failed to connect to Elasticsearch:[/] {ex.Message}\n");
	return 1;
}

// Run ES|QL queries against the cloud instance
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold]Running ES|QL Queries[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n\n");

// ============================================================================
// PRODUCT QUERIES
// ============================================================================
await RunQuerySection("Product Queries", async () =>
{
	// Top brands by average price
	await RunQuery(
		"Top 5 brands by average product price",
		client.Query<Product>()
			.GroupBy(p => p.Brand)
			.Select(g => new
			{
				Brand = g.Key,
				AvgPrice = g.Average(p => p.Price),
				ProductCount = g.Count()
			})
			.OrderByDescending(x => x.AvgPrice)
			.Take(5)
	);

	// Product stats
	await RunQuery(
		"Product statistics by brand (top 5)",
		client.Query<Product>()
			.Where(p => p.InStock)
			.GroupBy(p => p.Brand)
			.Select(g => new
			{
				Brand = g.Key,
				TotalProducts = g.Count(),
				AvgPrice = g.Average(p => p.Price),
				MinPrice = g.Min(p => p.Price),
				MaxPrice = g.Max(p => p.Price)
			})
			.Take(5)
	);
});

// ============================================================================
// ORDER QUERIES
// ============================================================================
await RunQuerySection("Order Queries", async () =>
{
	// Order statistics by status
	await RunQuery(
		"Order count and total by status",
		client.Query<Order>()
			.GroupBy(o => o.Status)
			.Select(g => new
			{
				Status = g.Key,
				OrderCount = g.Count(),
				TotalRevenue = g.Sum(o => o.TotalAmount)
			})
	);

	// High-value orders
	await RunQuery(
		"High-value orders (top 5)",
		client.Query<Order>()
			.Where(o => o.TotalAmount > 500)
			.OrderByDescending(o => o.TotalAmount)
			.Take(5)
	);
});

// ============================================================================
// CUSTOMER QUERIES
// ============================================================================
await RunQuerySection("Customer Queries", async () =>
{
	// Customer count by tier
	await RunQuery(
		"Customer count by tier",
		client.Query<Customer>()
			.GroupBy(c => c.Tier)
			.Select(g => new
			{
				Tier = g.Key,
				CustomerCount = g.Count()
			})
			.OrderByDescending(x => x.CustomerCount)
	);
});

// ============================================================================
// APPLICATION LOG QUERIES (Data Stream)
// ============================================================================
await RunQuerySection("Application Log Queries (Data Stream)", async () =>
{
	// Error logs
	await RunQuery(
		"Recent error logs",
		client.Query<ApplicationLog>()
			.Where(l => l.Level == LogLevel.Error || l.Level == LogLevel.Fatal)
			.OrderByDescending(l => l.Timestamp)
			.Take(5)
	);

	// Logs by service
	await RunQuery(
		"Log count by service",
		client.Query<ApplicationLog>()
			.GroupBy(l => l.ServiceName)
			.Select(g => new
			{
				Service = g.Key,
				LogCount = g.Count()
			})
			.OrderByDescending(x => x.LogCount)
			.Take(5)
	);

	// Logs by level
	await RunQuery(
		"Log count by level",
		client.Query<ApplicationLog>()
			.GroupBy(l => l.Level)
			.Select(g => new
			{
				Level = g.Key,
				Count = g.Count()
			})
	);
});

// ============================================================================
// APPLICATION METRIC QUERIES (Data Stream)
// ============================================================================
await RunQuerySection("Application Metric Queries (Data Stream)", async () =>
{
	// Latest system metrics (filter for system metricset to get CPU/memory values)
	await RunQuery(
		"Latest system metrics by host",
		client.Query<ApplicationMetric>()
			.Where(m => m.MetricSetName == "system")
			.OrderByDescending(m => m.Timestamp)
			.Select(m => new { m.HostName, m.CpuPercent, m.MemoryPercent, m.Timestamp })
			.Take(5)
	);

	// Average resource usage (system metrics only)
	await RunQuery(
		"Average resource usage by host",
		client.Query<ApplicationMetric>()
			.Where(m => m.MetricSetName == "system")
			.GroupBy(m => m.HostName)
			.Select(g => new
			{
				Host = g.Key,
				AvgCpu = g.Average(m => m.CpuPercent),
				AvgMemory = g.Average(m => m.MemoryPercent),
				MetricCount = g.Count()
			})
	);
});

// Summary
WriteOutput("\n[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold green]ES|QL queries completed![/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");

return 0;

// === Local functions ===

static void WriteOutput(string markup) => Terminal.WriteMarkup(markup);

async Task RunQuerySection(string title, Func<Task> queries)
{
	WriteOutput($"\n[bold cyan]{title}[/]\n");
	WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
	try
	{
		await queries();
	}
	catch (EsqlExecutionException ex)
	{
		WriteOutput($"[red]Section failed: {ex.Message}[/]\n");
		if (ex.ResponseBody != null)
		{
			try
			{
				using var doc = JsonDocument.Parse(ex.ResponseBody);
				if (doc.RootElement.TryGetProperty("error", out var error) &&
					error.TryGetProperty("reason", out var reason))
				{
					WriteOutput($"[gray]  Reason: {reason.GetString()}[/]\n");
				}
			}
			catch
			{
				// Ignore JSON parse errors
			}
		}
	}
}

async Task RunQuery<T>(string description, IQueryable<T> query)
{
	WriteOutput($"\n[yellow]>> {description}[/]\n");

	// Show the ES|QL query
	var esql = query.ToEsqlString();
	WriteOutput($"[gray]   Query: {esql.Replace("\n", " ").Replace("\r", "")}[/]\n");

	try
	{
		// Execute and show results
		if (query is IEsqlQueryable<T> esqlQueryable)
		{
			var results = await esqlQueryable.ToListAsync();

			if (results.Count == 0)
			{
				WriteOutput("   [gray](no results)[/]\n");
			}
			else
			{
				WriteOutput($"   [green]Found {results.Count} result(s):[/]\n");
				foreach (var result in results.Take(5))
				{
					WriteOutput($"   [white]• {FormatResult(result)}[/]\n");
				}
				if (results.Count > 5)
				{
					WriteOutput($"   [gray]... and {results.Count - 5} more[/]\n");
				}
			}
		}
	}
	catch (EsqlExecutionException ex)
	{
		WriteOutput($"   [red]Query failed: {ex.Message}[/]\n");
		if (ex.ResponseBody != null)
		{
			try
			{
				using var doc = JsonDocument.Parse(ex.ResponseBody);
				if (doc.RootElement.TryGetProperty("error", out var error) &&
					error.TryGetProperty("reason", out var reason))
				{
					WriteOutput($"   [gray]Reason: {reason.GetString()}[/]\n");
				}
			}
			catch
			{
				// Ignore JSON parse errors
			}
		}
	}
}

string FormatResult<T>(T result)
{
	if (result == null)
		return "(null)";

	// For anonymous types and complex objects, use JSON serialization
	try
	{
		return JsonSerializer.Serialize(result, jsonOptions);
	}
	catch
	{
		return result.ToString() ?? "(null)";
	}
}

static ITransport CreateTransport(string url, string apiKey)
{
	var uri = new Uri(url);
	var pool = new SingleNodePool(uri);
	var config = new TransportConfiguration(uri, new ApiKey(apiKey));

	return new DistributedTransport(config);
}

static (string url, string apiKey) GetElasticsearchConfiguration()
{
	// Try environment variables first (set by Aspire)
	var url = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL");
	var apiKey = Environment.GetEnvironmentVariable("ELASTICSEARCH_APIKEY");

	if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(apiKey))
		return (url, apiKey);

	// Fallback: read from Aspire's dotnet user secrets
	var config = new ConfigurationBuilder()
		.AddUserSecrets(typeof(Program).Assembly, optional: true)
		.Build();

	url ??= config["Parameters:ElasticsearchUrl"];
	apiKey ??= config["Parameters:ElasticsearchApiKey"];

	if (string.IsNullOrEmpty(url))
	{
		WriteOutput("[bold red]Elasticsearch URL not configured.[/]\n");
		WriteOutput("Set ELASTICSEARCH_URL env var or run:\n");
		WriteOutput("  [gray]dotnet user-secrets --project aspire set Parameters:ElasticsearchUrl <url>[/]\n");
		Environment.Exit(1);
	}

	if (string.IsNullOrEmpty(apiKey))
	{
		WriteOutput("[bold red]Elasticsearch API Key not configured.[/]\n");
		WriteOutput("Set ELASTICSEARCH_APIKEY env var or run:\n");
		WriteOutput("  [gray]dotnet user-secrets --project aspire set Parameters:ElasticsearchApiKey <key>[/]\n");
		Environment.Exit(1);
	}

	return (url, apiKey);
}
