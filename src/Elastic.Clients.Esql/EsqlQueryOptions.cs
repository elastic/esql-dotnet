// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Elastic.Clients.Esql;

/// <summary>Per-query options that override client defaults for the Elasticsearch transport executor.</summary>
public record EsqlQueryOptions
{
	/// <summary>Per-request transport configuration.</summary>
	public IRequestConfiguration? RequestConfiguration { get; init; }

	/// <summary>Whether to allow partial results when shards are unavailable.</summary>
	public bool? AllowPartialResults { get; init; }

	/// <summary>Whether to omit columns where every value is null from the response.</summary>
	public bool? DropNullColumns { get; init; }

	/// <summary>Locale for formatting (e.g., "en-US").</summary>
	public string? Locale { get; init; }

	/// <summary>Timezone for date operations (e.g., "UTC", "America/New_York").</summary>
	public string? TimeZone { get; init; }
}
