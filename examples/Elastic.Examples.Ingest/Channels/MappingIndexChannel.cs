// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elastic.Channels.Diagnostics;
using Elastic.Clients.Elasticsearch;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Mapping;
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

	/// <summary>Optional modifier for settings JSON.</summary>
	public Func<string, string>? Settings { get; init; }

	/// <summary>Optional modifier for mappings JSON.</summary>
	public Func<string, string>? Mappings { get; init; }

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
		var settingsTemplateName = $"{indexName}-settings";
		var mappingsTemplateName = $"{indexName}-mappings";
		var indexTemplateName = indexName;

		// Get base JSON from context
		var settingsJson = _options.Context.GetSettingsJson();
		var mappingsJson = _options.Context.GetMappingsJson();

		// Apply modifier functions if provided
		if (_options.Settings != null)
			settingsJson = _options.Settings(settingsJson);
		if (_options.Mappings != null)
			mappingsJson = _options.Mappings(mappingsJson);

		// Compute hash
		var hash = (_options.Settings == null && _options.Mappings == null)
			? _options.Context.Hash
			: ComputeHash(settingsJson, mappingsJson);

		// Check if index template exists and compare hash
		_options.OnBootstrapStatus?.Invoke($"Checking index template '{indexTemplateName}'...");

		if (IndexTemplateExists(indexTemplateName))
		{
			_options.OnBootstrapStatus?.Invoke($"Template '{indexTemplateName}' already exists");
			return false;
		}

		// Create settings component template
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{settingsTemplateName}'...");
		var settingsBody = CreateComponentTemplateBody("settings", settingsJson);
		if (!PutComponentTemplate(bootstrapMethod, settingsTemplateName, settingsBody))
			return false;

		// Create mappings component template
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{mappingsTemplateName}'...");
		var mappingsBody = CreateComponentTemplateBody("mappings", mappingsJson);
		if (!PutComponentTemplate(bootstrapMethod, mappingsTemplateName, mappingsBody))
			return false;

		// Create index template
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateIndexTemplateBody(indexName, settingsTemplateName, mappingsTemplateName, hash);
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
		var settingsTemplateName = $"{indexName}-settings";
		var mappingsTemplateName = $"{indexName}-mappings";
		var indexTemplateName = indexName;

		// Get base JSON from context
		var settingsJson = _options.Context.GetSettingsJson();
		var mappingsJson = _options.Context.GetMappingsJson();

		// Apply modifier functions if provided
		if (_options.Settings != null)
			settingsJson = _options.Settings(settingsJson);
		if (_options.Mappings != null)
			mappingsJson = _options.Mappings(mappingsJson);

		// Compute hash
		var hash = (_options.Settings == null && _options.Mappings == null)
			? _options.Context.Hash
			: ComputeHash(settingsJson, mappingsJson);

		// Check if index template exists
		_options.OnBootstrapStatus?.Invoke($"Checking index template '{indexTemplateName}'...");

		if (await IndexTemplateExistsAsync(indexTemplateName, ctx).ConfigureAwait(false))
		{
			_options.OnBootstrapStatus?.Invoke($"Template '{indexTemplateName}' already exists");
			return false;
		}

		// Create settings component template
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{settingsTemplateName}'...");
		var settingsBody = CreateComponentTemplateBody("settings", settingsJson);
		if (!await PutComponentTemplateAsync(bootstrapMethod, settingsTemplateName, settingsBody, ctx).ConfigureAwait(false))
			return false;

		// Create mappings component template
		_options.OnBootstrapStatus?.Invoke($"Creating component template '{mappingsTemplateName}'...");
		var mappingsBody = CreateComponentTemplateBody("mappings", mappingsJson);
		if (!await PutComponentTemplateAsync(bootstrapMethod, mappingsTemplateName, mappingsBody, ctx).ConfigureAwait(false))
			return false;

		// Create index template
		_options.OnBootstrapStatus?.Invoke($"Creating index template '{indexTemplateName}'...");
		var indexTemplate = CreateIndexTemplateBody(indexName, settingsTemplateName, mappingsTemplateName, hash);
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

	private static string CreateComponentTemplateBody(string section, string json)
	{
		var sectionContent = ExtractSectionContent(json, section);
		return $$"""
			{
				"template": {
					"{{section}}": {{sectionContent}}
				},
				"_meta": {
					"managed_by": "Elastic.Examples.Ingest"
				}
			}
			""";
	}

	private static string CreateIndexTemplateBody(string indexName, string settingsTemplate, string mappingsTemplate, string hash)
	{
		var indexPattern = indexName.EndsWith('*') ? indexName : $"{indexName}*";
		return $$"""
			{
				"index_patterns": ["{{indexPattern}}"],
				"composed_of": ["{{settingsTemplate}}", "{{mappingsTemplate}}"],
				"priority": 100,
				"_meta": {
					"hash": "{{hash}}",
					"managed_by": "Elastic.Examples.Ingest"
				}
			}
			""";
	}

	private static string ExtractSectionContent(string json, string section)
	{
		using var doc = JsonDocument.Parse(json);
		if (doc.RootElement.TryGetProperty(section, out var content))
			return content.GetRawText();

		return "{}";
	}

	private static string ComputeHash(string settingsJson, string mappingsJson)
	{
		var combined = $"v1:{Minify(settingsJson)}:{Minify(mappingsJson)}";
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
		return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
	}

	private static string Minify(string json)
	{
		using var doc = JsonDocument.Parse(json);
		return JsonSerializer.Serialize(doc.RootElement);
	}
}
