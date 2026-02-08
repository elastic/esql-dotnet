// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a POCO as an Elasticsearch index for attribute-based discovery (without a mapping context).
/// Applied directly to the domain type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IndexAttribute : Attribute
{
	/// <summary>Concrete index name (e.g., "projects").</summary>
	public string? Name { get; init; }

	/// <summary>Search pattern for queries (e.g., "logs-*").</summary>
	public string? SearchPattern { get; init; }

	/// <summary>ILM write alias (e.g., "logs-write").</summary>
	public string? WriteAlias { get; init; }

	/// <summary>ILM read alias (e.g., "logs-read").</summary>
	public string? ReadAlias { get; init; }
}

/// <summary>
/// Registers a type as an Elasticsearch index within an <see cref="ElasticsearchMappingContextAttribute"/> context.
/// Applied to the context class, not the domain type.
/// </summary>
/// <typeparam name="T">The domain type to map to an Elasticsearch index.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IndexAttribute<T> : Attribute where T : class
{
	/// <summary>Concrete index name (e.g., "projects").</summary>
	public string? Name { get; init; }

	/// <summary>ILM write alias (e.g., "logs-write").</summary>
	public string? WriteAlias { get; init; }

	/// <summary>ILM read alias (e.g., "logs-read").</summary>
	public string? ReadAlias { get; init; }

	/// <summary>Rolling date pattern (e.g., "yyyy.MM.dd" produces "logs-2025.01.31").</summary>
	public string? DatePattern { get; init; }

	/// <summary>Search pattern for queries (e.g., "logs-*").</summary>
	public string? SearchPattern { get; init; }

	/// <summary>Number of primary shards. Set to -1 (default) to omit for serverless compatibility.</summary>
	public int Shards { get; init; } = -1;

	/// <summary>Number of replica shards. Set to -1 (default) to omit for serverless compatibility.</summary>
	public int Replicas { get; init; } = -1;

	/// <summary>Refresh interval (e.g., "1s", "30s", "-1" for disabled).</summary>
	public string? RefreshInterval { get; init; }

	/// <summary>Dynamic mapping behavior.</summary>
	public bool Dynamic { get; init; } = true;

	/// <summary>Optional static class containing ConfigureAnalysis/ConfigureMappings methods.</summary>
	public Type? Configuration { get; init; }
}
