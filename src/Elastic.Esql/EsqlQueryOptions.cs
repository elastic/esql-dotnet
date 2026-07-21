// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>
/// Protocol-level ES|QL query options that map directly to fields of the <c>/_query</c> API.
/// Executor-specific settings (e.g. transport configuration) do not belong here; they travel as opaque executor options.
/// </summary>
public sealed record EsqlQueryOptions
{
	/// <summary>Whether to allow partial results when shards are unavailable. Maps to <c>allow_partial_results</c>.</summary>
	public bool? AllowPartialResults { get; init; }

	/// <summary>Whether to omit columns where every value is null from the response. Maps to <c>drop_null_columns</c>.</summary>
	public bool? DropNullColumns { get; init; }

	/// <summary>Locale for formatting (e.g. "en-US"). Maps to <c>locale</c>.</summary>
	public string? Locale { get; init; }

	/// <summary>Timezone for date operations (e.g. "UTC", "America/New_York"). Maps to <c>time_zone</c>.</summary>
	public string? TimeZone { get; init; }
}
