// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Execution;

/// <summary>
/// Request DTO for ES|QL query execution.
/// </summary>
public class EsqlRequest
{
	/// <summary>
	/// The ES|QL query string.
	/// </summary>
	[JsonPropertyName("query")]
	public string Query { get; set; } = string.Empty;

	/// <summary>
	/// Whether to return results in columnar format.
	/// </summary>
	[JsonPropertyName("columnar")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Columnar { get; set; }

	/// <summary>
	/// Whether to include profiling information.
	/// </summary>
	[JsonPropertyName("profile")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Profile { get; set; }

	/// <summary>
	/// Query parameters for parameterized queries.
	/// </summary>
	[JsonPropertyName("params")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IList<object>? Params { get; set; }

	/// <summary>
	/// Locale for formatting.
	/// </summary>
	[JsonPropertyName("locale")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Locale { get; set; }

	/// <summary>
	/// Timezone for date operations.
	/// </summary>
	[JsonPropertyName("time_zone")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? TimeZone { get; set; }
}
