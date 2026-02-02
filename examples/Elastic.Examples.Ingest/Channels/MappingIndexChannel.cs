// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Channels.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using BulkResponse = Elastic.Ingest.Elasticsearch.Serialization.BulkResponse;

namespace Elastic.Examples.Ingest.Channels;

/// <summary>Options for <see cref="MappingIndexChannel{T}"/>.</summary>
/// <typeparam name="T">The document type with Elasticsearch context.</typeparam>
public class MappingIndexChannelOptions<T>(ElasticsearchClient client) : IndexChannelOptions<T>(client.Transport)
	where T : class
{
	/// <summary>The Elasticsearch client (for convenience).</summary>
	public ElasticsearchClient Client { get; } = client;

	/// <summary>The Elasticsearch context for this type.</summary>
	public required ElasticsearchTypeContext Context { get; init; }

	/// <summary>Optional callback invoked during bootstrap.</summary>
	public Action<string>? OnBootstrapStatus { get; init; }
}

/// <summary>
/// IndexChannel that uses Elastic.Mapping source-generated context for template bootstrapping.
/// Extends the base IndexChannel with hash-based template change detection.
/// </summary>
/// <typeparam name="T">The document type with Elasticsearch context.</typeparam>
/// <param name="options">Channel configuration options.</param>
/// <param name="callbackListeners">Optional callback listeners.</param>
public class MappingIndexChannel<T>(
	MappingIndexChannelOptions<T> options,
	ICollection<IChannelCallbacks<T, BulkResponse>>? callbackListeners = null
) : IndexChannel<T>(MappingIndexChannel<T>.ConfigureOptions(options), callbackListeners)
	where T : class
{
	/// <summary>
	/// Creates a channel with auto-discovered context via interfaces.
	/// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - Factory method needs type parameter
	public static MappingIndexChannel<T> Create<TDoc>(ElasticsearchClient client)
		where TDoc : class, T, IHasElasticsearchContext
	{
#pragma warning restore CA1000
		var options = new MappingIndexChannelOptions<T>(client) { Context = TDoc.Context };
		return new MappingIndexChannel<T>(options);
	}
	private readonly MappingIndexChannelOptions<T> _options = options;

	private static MappingIndexChannelOptions<T> ConfigureOptions(MappingIndexChannelOptions<T> options)
	{
		// Auto-set IndexFormat from context if not explicitly set
		if (string.IsNullOrEmpty(options.IndexFormat))
		{
			var writeTarget = options.Context.IndexStrategy?.WriteTarget ?? typeof(T).Name.ToLowerInvariant();
			options.IndexFormat = writeTarget;
		}
		return options;
	}

	/// <inheritdoc />
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (bootstrapMethod == BootstrapMethod.None)
			return true;

		var indexName = GetIndexBaseName();
		var componentTemplateName = $"{indexName}-write";
		var indexTemplateName = indexName;

		// Check if index template exists
		_options.OnBootstrapStatus?.Invoke($"Checking index template '{indexTemplateName}'...");

		if (IndexTemplateExists(indexTemplateName))
		{
			_options.OnBootstrapStatus?.Invoke($"Template '{indexTemplateName}' already exists");
			return false;
		}

		// Create combined component template (settings + mappings together to pass analyzer validation)
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{componentTemplateName}'...");
		var combinedBody = CreateCombinedTemplateBody(_options.Context.GetSettingsJson(), _options.Context.GetMappingsJson());
		if (!PutComponentTemplate(bootstrapMethod, componentTemplateName, combinedBody))
			return false;

		// Create index template
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateIndexTemplateBody(indexName, componentTemplateName, _options.Context.Hash);
		if (!PutIndexTemplate(bootstrapMethod, indexTemplateName, indexTemplate))
			return false;

		_options.OnBootstrapStatus?.Invoke($"Bootstrap complete for '{indexTemplateName}'");
		return true;
	}

	/// <inheritdoc />
	public override async Task<bool> BootstrapElasticsearchAsync(
		BootstrapMethod bootstrapMethod,
		string? ilmPolicy = null,
		CancellationToken ctx = default
	)
	{
		if (bootstrapMethod == BootstrapMethod.None)
			return true;

		var indexName = GetIndexBaseName();
		var componentTemplateName = $"{indexName}-write";
		var indexTemplateName = indexName;

		// Check if index template exists
		_options.OnBootstrapStatus?.Invoke($"Checking index template '{indexTemplateName}'...");

		if (await IndexTemplateExistsAsync(indexTemplateName, ctx).ConfigureAwait(false))
		{
			_options.OnBootstrapStatus?.Invoke($"Template '{indexTemplateName}' already exists");
			return false;
		}

		// Create combined component template (settings + mappings together to pass analyzer validation)
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{componentTemplateName}'...");
		var combinedBody = CreateCombinedTemplateBody(_options.Context.GetSettingsJson(), _options.Context.GetMappingsJson());
		if (!await PutComponentTemplateAsync(bootstrapMethod, componentTemplateName, combinedBody, ctx).ConfigureAwait(false))
			return false;

		// Create index template
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateIndexTemplateBody(indexName, componentTemplateName, _options.Context.Hash);
		if (!await PutIndexTemplateAsync(bootstrapMethod, indexTemplateName, indexTemplate, ctx).ConfigureAwait(false))
			return false;

		_options.OnBootstrapStatus?.Invoke($"Bootstrap complete for '{indexTemplateName}'");
		return true;
	}

	private string GetIndexBaseName()
	{
		var writeTarget = _options.Context.IndexStrategy?.WriteTarget ?? typeof(T).Name.ToLowerInvariant();
		return writeTarget.TrimEnd('-');
	}

	private string CreateCombinedTemplateBody(string settingsJson, string mappingsJson)
	{
		// Merge analysis settings from ConfigureAnalysis if the type implements IHasAnalysisConfiguration
		var analysisSettings = GetAnalysisSettings();
		if (analysisSettings?.HasConfiguration == true)
			settingsJson = analysisSettings.MergeIntoSettings(settingsJson);

		using var settingsDoc = JsonDocument.Parse(settingsJson);
		using var mappingsDoc = JsonDocument.Parse(mappingsJson);

		var settingsContent = settingsDoc.RootElement.TryGetProperty("settings", out var s)
			? JsonNode.Parse(s.GetRawText())
			: new JsonObject();

		var mappingsContent = mappingsDoc.RootElement.TryGetProperty("mappings", out var m)
			? JsonNode.Parse(m.GetRawText())
			: new JsonObject();

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

		return template.ToJsonString();
	}

	private AnalysisSettings? GetAnalysisSettings()
	{
		// Check if type T has a ConfigureAnalysis static method (implements IHasAnalysisConfiguration)
		var configureMethod = typeof(T).GetMethod(
			"ConfigureAnalysis",
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
			null,
			[typeof(AnalysisBuilder)],
			null
		);

		if (configureMethod == null)
			return null;

		try
		{
			var builder = new AnalysisBuilder();
			var result = configureMethod.Invoke(null, [builder]);
			if (result is AnalysisBuilder returnedBuilder)
				return returnedBuilder.Build();
		}
		catch
		{
			// If reflection fails, continue without analysis
		}

		return null;
	}

	private static string CreateIndexTemplateBody(string indexName, string componentTemplate, string hash)
	{
		var indexPattern = indexName.EndsWith('*') ? indexName : $"{indexName}*";
		return $$"""
			{
				"index_patterns": ["{{indexPattern}}"],
				"composed_of": ["{{componentTemplate}}"],
				"priority": 100,
				"_meta": {
					"hash": "{{hash}}",
					"managed_by": "Elastic.Examples.Ingest"
				}
			}
			""";
	}
}
