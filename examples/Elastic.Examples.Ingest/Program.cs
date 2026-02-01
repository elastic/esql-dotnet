// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Examples.Domain.Models;
using Elastic.Examples.Ingest.Generators;
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

// Generate data
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
WriteOutput("[bold]Generating Data[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n\n");

WriteOutput("[cyan]Products:[/] Generating [yellow]1,000[/] documents...\n");
var products = ProductGenerator.Generate(1000);
WriteOutput("[bold green]Products:[/] Generated [yellow]1,000[/] documents\n\n");

WriteOutput("[cyan]Customers:[/] Generating [yellow]500[/] documents...\n");
var customers = CustomerGenerator.Generate(500);
WriteOutput("[bold green]Customers:[/] Generated [yellow]500[/] documents\n\n");

var productIds = products.Select(p => p.Id).ToList();
var customerIds = customers.Select(c => c.Id).ToList();

WriteOutput("[cyan]Orders:[/] Generating [yellow]5,000[/] documents...\n");
var orders = OrderGenerator.Generate(productIds, customerIds, 5000);
WriteOutput("[bold green]Orders:[/] Generated [yellow]5,000[/] documents\n\n");

var orderIds = orders.Select(o => o.Id).ToList();

WriteOutput("[cyan]Logs:[/] Generating [yellow]10,000[/] documents...\n");
var logs = ApplicationLogGenerator.Generate(orderIds, productIds, customerIds, 10000);
WriteOutput("[bold green]Logs:[/] Generated [yellow]10,000[/] documents\n\n");

WriteOutput("[cyan]Metrics:[/] Generating [yellow]5,000[/] documents...\n");
var metrics = ApplicationMetricGenerator.Generate(5000);
WriteOutput("[bold green]Metrics:[/] Generated [yellow]5,000[/] documents\n\n");

// Ingest data
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n");
WriteOutput("[bold]Ingesting Data[/]\n");
WriteOutput("[gray]───────────────────────────────────────────────────────────────────────[/]\n\n");

var productsIngested = await IngestDocuments(client, "products", products, p => p.Id);
var customersIngested = await IngestDocuments(client, "customers", customers, c => c.Id);
var ordersIngested = await IngestDocuments(client, "orders-write", orders, o => o.Id);
var logsIngested = await IngestToDataStream(client, "logs-ecommerce.app-default", logs);
var metricsIngested = await IngestToDataStream(client, "metrics-ecommerce.app-default", metrics);

// Summary
WriteOutput("\n[gray]═══════════════════════════════════════════════════════════════════════[/]\n");
WriteOutput("[bold green]Ingestion Complete![/]\n");
WriteOutput($"  [cyan]Products:[/]     [yellow]{productsIngested:N0}[/]\n");
WriteOutput($"  [cyan]Customers:[/]    [yellow]{customersIngested:N0}[/]\n");
WriteOutput($"  [cyan]Orders:[/]       [yellow]{ordersIngested:N0}[/]\n");
WriteOutput($"  [cyan]Logs:[/]         [yellow]{logsIngested:N0}[/]\n");
WriteOutput($"  [cyan]Metrics:[/]      [yellow]{metricsIngested:N0}[/]\n");
WriteOutput("[gray]═══════════════════════════════════════════════════════════════════════[/]\n");

return 0;

static void WriteOutput(string markup) => Terminal.WriteMarkup(markup);

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

static async Task<int> IngestDocuments<T>(
	ElasticsearchClient client,
	string indexName,
	IReadOnlyList<T> documents,
	Func<T, string> idSelector) where T : class
{
	var typeName = typeof(T).Name;
	WriteOutput($"[cyan]{typeName}s:[/] Creating index [gray]{indexName}[/]...\n");

	// Create index (ignore if exists)
	var createResponse = await client.Indices.CreateAsync(indexName);
	if (!createResponse.IsValidResponse && createResponse.ElasticsearchServerError?.Error?.Type != "resource_already_exists_exception")
		WriteOutput($"  [gray]Note: {createResponse.ElasticsearchServerError?.Error?.Reason ?? "Index may already exist"}[/]\n");

	WriteOutput($"[cyan]{typeName}s:[/] Ingesting [yellow]{documents.Count:N0}[/] documents...\n");

	const int batchSize = 500;
	var totalIngested = 0;

	for (var i = 0; i < documents.Count; i += batchSize)
	{
		var batch = documents.Skip(i).Take(batchSize).ToList();
		var operations = batch.Select(doc => new BulkIndexOperation<T>(doc) { Id = idSelector(doc) }).ToList();

		var bulkRequest = new BulkRequest(indexName) { Operations = [.. operations] };

		var response = await client.BulkAsync(bulkRequest);

		if (response.IsValidResponse)
		{
			totalIngested += batch.Count - (response.Errors ? response.ItemsWithErrors.Count() : 0);
			WriteOutput($"  [gray]Batch {(i / batchSize) + 1}: [green]{response.ApiCallDetails.HttpStatusCode}[/] ({batch.Count} docs)[/]\n");
		}
		else
		{
			WriteOutput($"  [red]Batch {(i / batchSize) + 1} failed: {response.DebugInformation}[/]\n");
		}
	}

	WriteOutput($"[bold green]{typeName}s:[/] Indexed [yellow]{totalIngested:N0}[/] documents\n\n");
	return totalIngested;
}

static async Task<int> IngestToDataStream<T>(
	ElasticsearchClient client,
	string dataStreamName,
	IReadOnlyList<T> documents) where T : class
{
	var typeName = typeof(T).Name;
	WriteOutput($"[cyan]{typeName}s:[/] Ingesting to data stream [gray]{dataStreamName}[/]...\n");

	const int batchSize = 500;
	var totalIngested = 0;

	for (var i = 0; i < documents.Count; i += batchSize)
	{
		var batch = documents.Skip(i).Take(batchSize).ToList();
		var operations = batch.Select(doc => new BulkCreateOperation<T>(doc)).ToList();

		var bulkRequest = new BulkRequest(dataStreamName) { Operations = [.. operations] };

		var response = await client.BulkAsync(bulkRequest);

		if (response.IsValidResponse)
		{
			totalIngested += batch.Count - (response.Errors ? response.ItemsWithErrors.Count() : 0);
			WriteOutput($"  [gray]Batch {(i / batchSize) + 1}: [green]{response.ApiCallDetails.HttpStatusCode}[/] ({batch.Count} docs)[/]\n");
		}
		else
		{
			WriteOutput($"  [red]Batch {(i / batchSize) + 1} failed: {response.DebugInformation}[/]\n");
		}
	}

	WriteOutput($"[bold green]{typeName}s:[/] Indexed [yellow]{totalIngested:N0}[/] documents\n\n");
	return totalIngested;
}
