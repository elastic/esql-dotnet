// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Clients.Esql.Execution;

/// <summary>Request DTO for ES|QL query execution.</summary>
internal sealed class EsqlRequest
{
	[JsonPropertyName("query")]
	public required string Query { get; init; }

	[JsonPropertyName("params")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? Params { get; init; }

	[JsonPropertyName("locale")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Locale { get; init; }

	[JsonPropertyName("time_zone")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? TimeZone { get; init; }
}
