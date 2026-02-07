// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>Default options applied to all ES|QL queries.</summary>
public record EsqlQueryDefaults
{
	/// <summary>Whether to return results in columnar format.</summary>
	public bool Columnar { get; init; }

	/// <summary>Whether to include profiling information.</summary>
	public bool IncludeProfile { get; init; }

	/// <summary>Default locale for formatting (e.g., "en-US").</summary>
	public string? Locale { get; init; }

	/// <summary>Default timezone for date operations (e.g., "UTC", "America/New_York").</summary>
	public string? TimeZone { get; init; }
}
