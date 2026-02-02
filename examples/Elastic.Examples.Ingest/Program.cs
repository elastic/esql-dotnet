// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Examples.Ingest.Ingestors;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using XenoAtom.Terminal;

Terminal.Title = "Elastic.Examples.Ingest";
WriteOutput("[bold]Elastic.Examples.Ingest[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n\n");

// Get Elasticsearch connection configuration
var (url, apiKey) = GetElasticsearchConfiguration();

WriteOutput($"[cyan]Elasticsearch:[/] {url}\n\n");

// Create Elasticsearch client
var settings = new ElasticsearchClientSettings(new Uri(url))
	.Authentication(new ApiKey(apiKey))
	.EnableDebugMode();

var client = new ElasticsearchClient(settings);

// Verify connection
WriteOutput("[cyan]Verifying connection...[/]\n");
var pingResponse = await client.PingAsync();
if (!pingResponse.IsValidResponse)
{
	WriteOutput($"[bold red]Failed to connect to Elasticsearch:[/] {pingResponse.DebugInformation}\n");
	return 1;
}
WriteOutput("[bold green]Connected successfully![/]\n\n");

// Clean up existing indices and data streams
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold]Cleaning Up Existing Data[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n\n");

await CleanupIndicesAndTemplates(client);

// Create callbacks for terminal output
var callbacks = CreateCallbacks();

// Ingest data using the new ingestors
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold]Ingesting Data[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n\n");

// Products
WriteOutput("[bold cyan]Products[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
var productIngestor = new ProductIngestor();
var productResult = await productIngestor.IngestAsync(client, callbacks);
WriteOutput("\n");

// Customers
WriteOutput("[bold cyan]Customers[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
var customerIngestor = new CustomerIngestor();
var customerResult = await customerIngestor.IngestAsync(client, callbacks);
WriteOutput("\n");

// Orders (needs product and customer IDs)
WriteOutput("[bold cyan]Orders[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
var orderIngestor = new OrderIngestor();
var orderResult = await orderIngestor.IngestAsync(
	client,
	productResult.ProductIds,
	customerResult.CustomerIds,
	callbacks
);
WriteOutput("\n");

// Logs (can use IDs from prior ingestors for labeling)
WriteOutput("[bold cyan]Application Logs[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
var logIngestor = new ApplicationLogIngestor();
var logResult = await logIngestor.IngestAsync(
	client,
	orderResult.OrderIds,
	productResult.ProductIds,
	customerResult.CustomerIds,
	callbacks
);
WriteOutput("\n");

// Metrics
WriteOutput("[bold cyan]Application Metrics[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
var metricIngestor = new ApplicationMetricIngestor();
var metricResult = await metricIngestor.IngestAsync(client, callbacks);
WriteOutput("\n");

// Summary
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold green]Ingestion Complete![/]\n");
WriteOutput($"  [cyan]Products:[/]     [yellow]{productResult.Indexed:N0}[/] indexed, [red]{productResult.Failed}[/] failed\n");
WriteOutput($"  [cyan]Customers:[/]    [yellow]{customerResult.Indexed:N0}[/] indexed, [red]{customerResult.Failed}[/] failed\n");
WriteOutput($"  [cyan]Orders:[/]       [yellow]{orderResult.Indexed:N0}[/] indexed, [red]{orderResult.Failed}[/] failed\n");
WriteOutput($"  [cyan]Logs:[/]         [yellow]{logResult.Indexed:N0}[/] indexed, [red]{logResult.Failed}[/] failed\n");
WriteOutput($"  [cyan]Metrics:[/]      [yellow]{metricResult.Indexed:N0}[/] indexed, [red]{metricResult.Failed}[/] failed\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");

var totalIndexed = productResult.Indexed + customerResult.Indexed + orderResult.Indexed + logResult.Indexed + metricResult.Indexed;
var totalFailed = productResult.Failed + customerResult.Failed + orderResult.Failed + logResult.Failed + metricResult.Failed;
WriteOutput($"[bold]Total:[/] [green]{totalIndexed:N0}[/] indexed, [red]{totalFailed:N0}[/] failed\n");

return totalFailed > 0 ? 1 : 0;

// === Local functions ===

static void WriteOutput(string markup) => Terminal.WriteMarkup(markup);

IngestCallbacks CreateCallbacks() =>
	new(
		OnStatus: msg => WriteOutput($"  [gray]{msg}[/]\n"),
		OnInfo: msg => WriteOutput($"  [cyan]{msg}[/]\n"),
		OnProgress: (current, total) =>
		{
			var pct = total > 0 ? (current * 100 / total) : 0;
			WriteOutput($"  [yellow]Progress: {current:N0}/{total:N0} ({pct}%)[/]\r");
		},
		OnComplete: (indexed, failed) =>
		{
			WriteOutput($"  [green]✓ Indexed: {indexed:N0}[/]");
			if (failed > 0)
				WriteOutput($", [red]Failed: {failed:N0}[/]");
			WriteOutput("\n");
		},
		OnError: msg => WriteOutput($"  [bold red]Error: {msg}[/]\n")
	);

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

async Task CleanupIndicesAndTemplates(ElasticsearchClient client)
{
	// Data streams to delete
	string[] dataStreams = ["logs-ecommerce.app-production", "metrics-ecommerce.app-production"];

	// Index patterns to delete
	string[] indexPatterns = ["products*", "customers*", "orders-*"];

	// Index templates to delete
	string[] indexTemplates =
	[
		"products-write", "customers-write", "orders-write",
		"logs-ecommerce.app-production", "metrics-ecommerce.app-production"
	];

	// Component templates to delete
	string[] componentTemplates =
	[
		"products-write-write", "customers-write-write", "orders-write-write",
		"logs-ecommerce.app-production-write", "metrics-ecommerce.app-production-write"
	];

	// Delete data streams
	foreach (var ds in dataStreams)
	{
		WriteOutput($"  [gray]Deleting data stream '{ds}'...[/]");
		var response = await client.Indices.DeleteDataStreamAsync(ds);
		WriteOutput(response.IsValidResponse ? " [green]✓[/]\n" : " [yellow]skipped[/]\n");
	}

	// Delete indices
	foreach (var pattern in indexPatterns)
	{
		WriteOutput($"  [gray]Deleting indices '{pattern}'...[/]");
		var response = await client.Indices.DeleteAsync(pattern);
		WriteOutput(response.IsValidResponse ? " [green]✓[/]\n" : " [yellow]skipped[/]\n");
	}

	// Delete index templates
	foreach (var template in indexTemplates)
	{
		WriteOutput($"  [gray]Deleting index template '{template}'...[/]");
		var response = await client.Indices.DeleteIndexTemplateAsync(template);
		WriteOutput(response.IsValidResponse ? " [green]✓[/]\n" : " [yellow]skipped[/]\n");
	}

	// Delete component templates
	foreach (var template in componentTemplates)
	{
		WriteOutput($"  [gray]Deleting component template '{template}'...[/]");
		var response = await client.Cluster.DeleteComponentTemplateAsync(template);
		WriteOutput(response.IsValidResponse ? " [green]✓[/]\n" : " [yellow]skipped[/]\n");
	}

	WriteOutput("\n");
}
