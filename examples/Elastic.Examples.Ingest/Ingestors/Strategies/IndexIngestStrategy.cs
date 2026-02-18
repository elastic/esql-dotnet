// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;
using Elastic.Channels;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Examples.Ingest.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using Elastic.Transport;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Elastic.Examples.Ingest.Ingestors.Strategies;

/// <summary>
/// Ingest strategy for traditional indices with explicit document IDs.
/// Supports both Elastic.Ingest channel and Bulk API approaches.
/// </summary>
public static class IndexIngestStrategy
{
	/// <summary>Ingests documents using MappingIndexChannel.</summary>
	public static async Task<(int indexed, int failed)> IngestViaChannelAsync<T>(
		ElasticsearchClient client,
		IReadOnlyList<T> documents,
		ElasticsearchTypeContext context,
		int batchSize,
		Func<T, string> idLookup,
		IngestCallbacks callbacks,
		CancellationToken ct) where T : class
	{
		var options = new MappingIndexChannelOptions<T>(client)
		{
			Context = context,
			BulkOperationIdLookup = idLookup,
			OnBootstrapStatus = callbacks.OnStatus,
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = batchSize }
		};

		var channel = new MappingIndexChannel<T>(options);
		try
		{
			_ = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, null, ct);

			var total = documents.Count;
			var written = 0;

			foreach (var document in documents)
			{
				if (!channel.TryWrite(document))
					continue;

				written++;
				if (written % batchSize == 0)
					callbacks.OnProgress(written, total);
			}

			_ = await channel.WaitForDrainAsync(null, ct);
			_ = await channel.RefreshAsync(ct);

			return (written, 0);
		}
		finally
		{
			_ = channel.TryComplete();
		}
	}

	/// <summary>Ingests documents using Bulk API with BulkIndexOperation.</summary>
	public static async Task<(int indexed, int failed)> IngestViaBulkApiAsync<T>(
		ElasticsearchClient client,
		IReadOnlyList<T> documents,
		ElasticsearchTypeContext context,
		int batchSize,
		Func<T, string> idLookup,
		IngestCallbacks callbacks,
		CancellationToken ct) where T : class
	{
		await BootstrapTemplatesAsync<T>(client, context, callbacks, ct);

		var indexed = 0;
		var failed = 0;
		var indexName = context.IndexStrategy?.WriteTarget ?? typeof(T).Name.ToLowerInvariant();

		callbacks.OnStatus($"Ensuring index '{indexName}' exists...");
		var createResponse = await client.Indices.CreateAsync(indexName, cancellationToken: ct);
		if (!createResponse.IsValidResponse &&
			createResponse.ElasticsearchServerError?.Error?.Type != "resource_already_exists_exception")
		{
			callbacks.OnStatus($"Note: {createResponse.ElasticsearchServerError?.Error?.Reason ?? "Index may already exist"}");
		}

		var total = documents.Count;
		for (var i = 0; i < documents.Count; i += batchSize)
		{
			ct.ThrowIfCancellationRequested();

			var batch = documents.Skip(i).Take(batchSize).ToList();
			var operations = batch.Select(d => (IBulkOperation)new BulkIndexOperation<T>(d) { Id = idLookup(d) });

			var bulkRequest = new BulkRequest(indexName) { Operations = [.. operations] };
			var response = await client.BulkAsync(bulkRequest, ct);

			if (response.IsValidResponse)
			{
				var batchFailed = response.Errors ? response.ItemsWithErrors.Count() : 0;
				indexed += batch.Count - batchFailed;
				failed += batchFailed;
			}
			else
			{
				failed += batch.Count;
				callbacks.OnError($"Batch failed: {response.ElasticsearchServerError?.Error?.Reason}");
			}

			callbacks.OnProgress(Math.Min(i + batchSize, total), total);
		}

		return (indexed, failed);
	}

	private static async Task BootstrapTemplatesAsync<T>(
		ElasticsearchClient client,
		ElasticsearchTypeContext context,
		IngestCallbacks callbacks,
		CancellationToken ct) where T : class
	{
		var indexName = context.IndexStrategy?.WriteTarget?.TrimEnd('-') ?? "index";
		var componentTemplateName = $"{indexName}-write";
		var indexTemplateName = indexName;

		callbacks.OnStatus($"Checking index template '{indexTemplateName}'...");

		var existingHash = await GetTemplateHashAsync(client, indexTemplateName, ct);
		if (existingHash == context.Hash)
		{
			callbacks.OnStatus($"Template '{indexTemplateName}' is up to date (hash: {context.Hash[..8]}...)");
			return;
		}

		// Create combined component template (settings + mappings together to pass analyzer validation)
		callbacks.OnStatus($"Creating component template '{componentTemplateName}'...");
		await CreateCombinedComponentTemplateAsync(client, componentTemplateName, context, ct);

		callbacks.OnStatus($"Creating index template '{indexTemplateName}'...");
		await CreateIndexTemplateAsync(client, indexTemplateName, componentTemplateName, context, ct);
	}

	private static async Task<string?> GetTemplateHashAsync(ElasticsearchClient client, string templateName, CancellationToken ct)
	{
		try
		{
			var endpointPath = new EndpointPath(HttpMethod.GET, $"_index_template/{templateName}");
			var response = await client.Transport.RequestAsync<StringResponse>(
				in endpointPath,
				null,
				null,
				null,
				ct
			);

			if (!response.ApiCallDetails.HasSuccessfulStatusCode)
				return null;

			using var doc = JsonDocument.Parse(response.Body);
			if (doc.RootElement.TryGetProperty("index_templates", out var templates) &&
				templates.GetArrayLength() > 0)
			{
				var template = templates[0];
				if (template.TryGetProperty("index_template", out var indexTemplate) &&
					indexTemplate.TryGetProperty("_meta", out var meta) &&
					meta.TryGetProperty("hash", out var hashElement))
				{
					return hashElement.GetString();
				}
			}
		}
		catch
		{
			// Template doesn't exist
		}

		return null;
	}

	private static async Task CreateCombinedComponentTemplateAsync(
		ElasticsearchClient client,
		string name,
		ElasticsearchTypeContext context,
		CancellationToken ct)
	{
		var settingsJson = context.GetSettingsJson();
		var mappingsJson = context.GetMappingsJson();

		// Merge analysis settings from ConfigureAnalysis delegate if available
		var analysisSettings = GetAnalysisSettings(context);
		if (analysisSettings?.HasConfiguration == true)
			settingsJson = analysisSettings.MergeIntoSettings(settingsJson);

		var settingsContent = JsonNode.Parse(settingsJson);
		var mappingsContent = JsonNode.Parse(mappingsJson);

		var template = new JsonObject
		{
			["template"] = new JsonObject
			{
				["settings"] = settingsContent,
				["mappings"] = mappingsContent
			},
			["_meta"] = new JsonObject
			{
				["managed_by"] = "Elastic.Examples.Ingest"
			}
		};

		var endpointPath = new EndpointPath(HttpMethod.PUT, $"_component_template/{name}");
		_ = await client.Transport.RequestAsync<StringResponse>(
			in endpointPath,
			PostData.String(template.ToJsonString()),
			null,
			null,
			ct
		);
	}

	private static AnalysisSettings? GetAnalysisSettings(ElasticsearchTypeContext context)
	{
		var configure = context.ConfigureAnalysis;
		if (configure == null)
			return null;

		var builder = new AnalysisBuilder();
		var result = configure(builder);
		return result.Build();
	}

	private static async Task CreateIndexTemplateAsync(
		ElasticsearchClient client,
		string name,
		string componentTemplate,
		ElasticsearchTypeContext context,
		CancellationToken ct)
	{
		var indexPattern = context.IndexStrategy?.WriteTarget ?? name;
		if (!indexPattern.EndsWith('*'))
			indexPattern += "*";

		var body = $$"""
			{
				"index_patterns": ["{{indexPattern}}"],
				"composed_of": ["{{componentTemplate}}"],
				"priority": 100,
				"_meta": {
					"hash": "{{context.Hash}}",
					"managed_by": "Elastic.Examples.Ingest"
				}
			}
			""";

		var endpointPath = new EndpointPath(HttpMethod.PUT, $"_index_template/{name}");
		_ = await client.Transport.RequestAsync<StringResponse>(
			in endpointPath,
			PostData.String(body),
			null,
			null,
			ct
		);
	}
}
