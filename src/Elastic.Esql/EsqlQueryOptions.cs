// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Esql;

/// <summary>Per-query options that override client defaults.</summary>
public record EsqlQueryOptions
{
	/// <summary>Locale for formatting (e.g., "en-US").</summary>
	public string? Locale { get; init; }

	/// <summary>Timezone for date operations (e.g., "UTC", "America/New_York").</summary>
	public string? TimeZone { get; init; }

	/// <summary>Query parameters for parameterized queries (for ? placeholders in raw ES|QL).</summary>
	public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? Parameters { get; init; }
}
