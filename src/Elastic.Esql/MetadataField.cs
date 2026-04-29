// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>
/// Bitfield of ES|QL document metadata fields that can be requested via the <c>METADATA</c>
/// directive of the <c>FROM</c> command.
/// </summary>
[Flags]
public enum MetadataField
{
	/// <summary>No metadata.</summary>
	None = 0,

	/// <summary>The <c>_id</c> metadata field (keyword): unique document id.</summary>
	Id = 1 << 0,

	/// <summary>The <c>_ignored</c> metadata field (keyword[]): names every field that was ignored at index time.</summary>
	Ignored = 1 << 1,

	/// <summary>The <c>_index</c> metadata field (keyword): index name.</summary>
	Index = 1 << 2,

	/// <summary>The <c>_index_mode</c> metadata field (keyword): index mode (e.g. <c>standard</c>, <c>lookup</c>, <c>logsdb</c>).</summary>
	IndexMode = 1 << 3,

	/// <summary>The <c>_score</c> metadata field (float): query relevance score, when enabled.</summary>
	Score = 1 << 4,

	/// <summary>The <c>_size</c> metadata field (integer): size in bytes of the original <c>_source</c> field.</summary>
	Size = 1 << 5,

	/// <summary>The <c>_source</c> metadata field: original document body as a JSON object.</summary>
	Source = 1 << 6,

	/// <summary>The <c>_version</c> metadata field (long): document version number.</summary>
	Version = 1 << 7,

	/// <summary>All supported metadata fields.</summary>
	All = Id | Ignored | Index | IndexMode | Score | Size | Source | Version
}
