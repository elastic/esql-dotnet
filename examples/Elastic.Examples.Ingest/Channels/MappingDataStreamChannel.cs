// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Channels.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using BulkResponse = Elastic.Ingest.Elasticsearch.Serialization.BulkResponse;
using DataStreamName = Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName;

namespace Elastic.Examples.Ingest.Channels;

/// <summary>Options for <see cref="MappingDataStreamChannel{T}"/>.</summary>
/// <typeparam name="T">The document type with Elasticsearch context.</typeparam>
public class MappingDataStreamChannelOptions<T>(ElasticsearchClient client) : DataStreamChannelOptions<T>(client.Transport)
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
/// DataStreamChannel that uses Elastic.Mapping source-generated context for template bootstrapping.
/// Extends the base DataStreamChannel with hash-based template change detection.
/// </summary>
/// <typeparam name="T">The document type with Elasticsearch context.</typeparam>
/// <param name="options">Channel configuration options.</param>
/// <param name="callbackListeners">Optional callback listeners.</param>
public class MappingDataStreamChannel<T>(
	MappingDataStreamChannelOptions<T> options,
	ICollection<IChannelCallbacks<T, BulkResponse>>? callbackListeners = null
) : DataStreamChannel<T>(MappingDataStreamChannel<T>.ConfigureOptions(options), callbackListeners)
	where T : class
{
	private readonly MappingDataStreamChannelOptions<T> _options = options;

	private static MappingDataStreamChannelOptions<T> ConfigureOptions(MappingDataStreamChannelOptions<T> options)
	{
		// Auto-set DataStream from context if not explicitly set
		if (options.DataStream == null)
		{
			var strategy = options.Context.IndexStrategy;
			if (strategy?.DataStreamName != null)
			{
				// Parse the data stream name (format: type-dataset-namespace)
				var parts = strategy.DataStreamName.Split('-', 3);
				if (parts.Length >= 2)
				{
					options.DataStream = new DataStreamName(
						parts[0],
						parts.Length > 1 ? parts[1] : "default",
						parts.Length > 2 ? parts[2] : "default"
					);
				}
			}
			else if (strategy?.Type != null)
			{
				options.DataStream = new DataStreamName(
					strategy.Type,
					strategy.Dataset ?? "default",
					strategy.Namespace ?? "default"
				);
			}
		}
		return options;
	}

	/// <inheritdoc />
	public override bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (bootstrapMethod == BootstrapMethod.None)
			return true;

		var dataStreamName = GetDataStreamName();
		var componentTemplateName = $"{dataStreamName}-write";
		var indexTemplateName = dataStreamName;

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

		// Create index template with data stream configuration
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateDataStreamIndexTemplateBody(dataStreamName, componentTemplateName, _options.Context.Hash);
		if (!PutIndexTemplate(bootstrapMethod, indexTemplateName, indexTemplate))
			return false;

		_options.OnBootstrapStatus?.Invoke($"Bootstrap complete for data stream '{dataStreamName}'");
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

		var dataStreamName = GetDataStreamName();
		var componentTemplateName = $"{dataStreamName}-write";
		var indexTemplateName = dataStreamName;

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

		// Create index template with data stream configuration
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateDataStreamIndexTemplateBody(dataStreamName, componentTemplateName, _options.Context.Hash);
		if (!await PutIndexTemplateAsync(bootstrapMethod, indexTemplateName, indexTemplate, ctx).ConfigureAwait(false))
			return false;

		_options.OnBootstrapStatus?.Invoke($"Bootstrap complete for data stream '{dataStreamName}'");
		return true;
	}

	private string GetDataStreamName()
	{
		var strategy = _options.Context.IndexStrategy;
		if (strategy?.DataStreamName != null)
			return strategy.DataStreamName;

		if (strategy?.Type != null && strategy?.Dataset != null)
			return $"{strategy.Type}-{strategy.Dataset}-{strategy.Namespace ?? "default"}";

		return $"logs-{typeof(T).Name.ToLowerInvariant()}-default";
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

	private string CreateDataStreamIndexTemplateBody(string dataStreamName, string componentTemplate, string hash)
	{
		var type = _options.Context.IndexStrategy?.Type ?? "logs";

		// ECS templates based on type
		string[] ecsTemplates = type switch
		{
			"logs" => ["logs@mappings", "logs@settings", "data-streams-mappings"],
			"metrics" => ["metrics@mappings", "metrics@settings", "data-streams-mappings"],
			_ => ["data-streams-mappings"]
		};

		// Put custom template LAST so it overrides ECS defaults
		var allTemplates = ecsTemplates.Append(componentTemplate).Select(t => $"\"{t}\"");

		return $$"""
			{
				"index_patterns": ["{{dataStreamName}}*"],
				"data_stream": {},
				"composed_of": [{{string.Join(", ", allTemplates)}}],
				"priority": 200,
				"_meta": {
					"hash": "{{hash}}",
					"managed_by": "Elastic.Examples.Ingest"
				}
			}
			""";
	}
}
