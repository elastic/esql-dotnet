// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Specifies Elasticsearch index configuration for a type.
/// Use for traditional indices with aliases, patterns, and shard configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class IndexAttribute : Attribute
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

	/// <summary>Number of primary shards.</summary>
	public int Shards { get; init; } = 1;

	/// <summary>Number of replica shards.</summary>
	public int Replicas { get; init; } = 1;

	/// <summary>Refresh interval (e.g., "1s", "30s", "-1" for disabled).</summary>
	public string? RefreshInterval { get; init; }

	/// <summary>Dynamic mapping behavior.</summary>
	public bool Dynamic { get; init; } = true;
}
