// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>
/// The wire-level response format for raw ES|QL queries.
/// Maps to the <c>format</c> request body field accepted by <c>/_query</c> and <c>/_query/async</c>.
/// </summary>
public enum EsqlFormat
{
	/// <summary>JSON (default).</summary>
	Json,

	/// <summary>Comma-separated values.</summary>
	Csv,

	/// <summary>Tab-separated values.</summary>
	Tsv,

	/// <summary>Plain text (human-readable table).</summary>
	Txt,

	/// <summary>Apache Arrow IPC stream.</summary>
	Arrow,

	/// <summary>SMILE (binary JSON variant).</summary>
	Smile,

	/// <summary>CBOR (Concise Binary Object Representation).</summary>
	Cbor,

	/// <summary>YAML.</summary>
	Yaml
}

internal static class EsqlFormatExtensions
{
	/// <summary>Returns the wire-format identifier (e.g. <c>"csv"</c>) used in the request body and on the poll URL.</summary>
	public static string GetFormatName(this EsqlFormat format) =>
		format switch
		{
			EsqlFormat.Json => "json",
			EsqlFormat.Csv => "csv",
			EsqlFormat.Tsv => "tsv",
			EsqlFormat.Txt => "txt",
			EsqlFormat.Arrow => "arrow",
			EsqlFormat.Smile => "smile",
			EsqlFormat.Cbor => "cbor",
			EsqlFormat.Yaml => "yaml",
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
		};

	/// <summary>Returns the HTTP <c>Accept</c> media type that matches the format (e.g. <c>"text/csv"</c>).</summary>
	public static string GetMediaType(this EsqlFormat format) =>
		format switch
		{
			EsqlFormat.Json => "application/json",
			EsqlFormat.Csv => "text/csv",
			EsqlFormat.Tsv => "text/tab-separated-values",
			EsqlFormat.Txt => "text/plain",
			EsqlFormat.Arrow => "application/vnd.apache.arrow.stream",
			EsqlFormat.Smile => "application/smile",
			EsqlFormat.Cbor => "application/cbor",
			EsqlFormat.Yaml => "application/yaml",
			_ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
		};
}
