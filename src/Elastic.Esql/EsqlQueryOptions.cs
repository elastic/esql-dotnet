// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>Per-query options that override client defaults.</summary>
public record EsqlQueryOptions
{
	/// <summary>Whether to return results in columnar format.</summary>
	public bool? Columnar { get; init; }

	/// <summary>Whether to include profiling information.</summary>
	public bool? IncludeProfile { get; init; }

	/// <summary>Locale for formatting (e.g., "en-US").</summary>
	public string? Locale { get; init; }

	/// <summary>Timezone for date operations (e.g., "UTC", "America/New_York").</summary>
	public string? TimeZone { get; init; }

	/// <summary>Query parameters for parameterized queries (for ? placeholders in raw ES|QL).</summary>
	public IReadOnlyList<object>? Parameters { get; init; }
}
