// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Execution;

/// <summary>
/// Response DTO from ES|QL query execution.
/// </summary>
public class EsqlResponse
{
	/// <summary>
	/// Column definitions.
	/// </summary>
	[JsonPropertyName("columns")]
	public List<EsqlColumn> Columns { get; set; } = [];

	/// <summary>
	/// Result values (row-based format).
	/// </summary>
	[JsonPropertyName("values")]
	public List<List<object?>> Values { get; set; } = [];

	/// <summary>
	/// Whether there are more results.
	/// </summary>
	[JsonPropertyName("_async")]
	public bool IsAsync { get; set; }

	/// <summary>
	/// Query ID for async queries.
	/// </summary>
	[JsonPropertyName("id")]
	public string? Id { get; set; }

	/// <summary>
	/// Whether the query is still running.
	/// </summary>
	[JsonPropertyName("is_running")]
	public bool IsRunning { get; set; }

	/// <summary>
	/// Profiling information.
	/// </summary>
	[JsonPropertyName("profile")]
	public EsqlProfile? Profile { get; set; }
}

/// <summary>
/// Column definition in ES|QL response.
/// </summary>
public class EsqlColumn
{
	/// <summary>
	/// Column name.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Column type.
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Profiling information from ES|QL execution.
/// </summary>
public class EsqlProfile
{
	/// <summary>
	/// Time spent on various operations.
	/// </summary>
	[JsonPropertyName("took")]
	public long Took { get; set; }

	/// <summary>
	/// Driver profile information.
	/// </summary>
	[JsonPropertyName("drivers")]
	public List<object>? Drivers { get; set; }
}
