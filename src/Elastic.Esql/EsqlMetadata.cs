// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Esql;

/// <summary>
/// Marker class exposing ES|QL document metadata fields as strongly-typed members for use in
/// LINQ expressions. Each property emits the corresponding underscore-prefixed ES|QL identifier
/// (e.g. <see cref="Id"/> -> <c>_id</c>) when referenced from a <c>Where</c>, <c>OrderBy</c>,
/// <c>Select</c>, or <c>Fuse</c> lambda. The matching <see cref="MetadataField"/> must be
/// requested on the <c>From</c> command, otherwise translation throws.
/// </summary>
public static class EsqlMetadata
{
	/// <summary>The <c>_id</c> metadata field (keyword): unique document id.</summary>
	public static string Id => Throw<string>();

	/// <summary>The <c>_ignored</c> metadata field (keyword[]): names every field that was ignored at index time.</summary>
	public static string[] Ignored => Throw<string[]>();

	/// <summary>The <c>_index</c> metadata field (keyword): index name.</summary>
	public static string Index => Throw<string>();

	/// <summary>The <c>_index_mode</c> metadata field (keyword): index mode (e.g. <c>standard</c>, <c>lookup</c>, <c>logsdb</c>).</summary>
	public static string IndexMode => Throw<string>();

	/// <summary>The <c>_score</c> metadata field (float): query relevance score, when enabled.</summary>
	public static float Score => Throw<float>();

	/// <summary>The <c>_size</c> metadata field (integer): size in bytes of the original <c>_source</c> field.</summary>
	public static int Size => Throw<int>();

	/// <summary>The <c>_source</c> metadata field: original document body as a JSON object.</summary>
	public static JsonObject Source => Throw<JsonObject>();

	/// <summary>The <c>_version</c> metadata field (long): document version number.</summary>
	public static long Version => Throw<long>();

	/// <summary>The <c>_fork</c> discriminator column added by the <c>FORK</c> command (e.g. <c>fork1</c>, <c>fork2</c>).</summary>
	public static string Fork => Throw<string>();

	/// <summary>
	/// Projects the <c>_source</c> metadata field as a typed value of <typeparamref name="T"/> by
	/// deserialising the original document body into the destination type.
	/// </summary>
	public static T SourceAs<T>() => Throw<T>();

	private static T Throw<T>() =>
		throw new InvalidOperationException("EsqlMetadata members are markers for use inside LINQ expressions only.");
}
