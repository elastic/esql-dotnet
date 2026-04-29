// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Core;

/// <summary>
/// Maps between <see cref="MetadataField"/> flag values, their ES|QL identifiers
/// (e.g. <c>_id</c>, <c>_index_mode</c>), and the corresponding member names on
/// <see cref="EsqlMetadata"/>.
/// </summary>
internal static class MetadataFieldHelper
{
	/// <summary>Returns the ES|QL identifier for a single <see cref="MetadataField"/> flag.</summary>
	public static string ToEsqlName(MetadataField field) =>
		field switch
		{
			MetadataField.Id => "_id",
			MetadataField.Ignored => "_ignored",
			MetadataField.Index => "_index",
			MetadataField.IndexMode => "_index_mode",
			MetadataField.Score => "_score",
			MetadataField.Size => "_size",
			MetadataField.Source => "_source",
			MetadataField.Version => "_version",
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a single metadata field flag.")
		};

	/// <summary>
	/// Returns the ES|QL identifier for the named member of <see cref="EsqlMetadata"/>
	/// (e.g. <c>nameof(EsqlMetadata.Id)</c> -> <c>_id</c>). Returns <see langword="null"/>
	/// if the name does not correspond to a known metadata member.
	/// </summary>
	public static string? FromMemberName(string memberName) =>
		memberName switch
		{
			nameof(EsqlMetadata.Id) => "_id",
			nameof(EsqlMetadata.Ignored) => "_ignored",
			nameof(EsqlMetadata.Index) => "_index",
			nameof(EsqlMetadata.IndexMode) => "_index_mode",
			nameof(EsqlMetadata.Score) => "_score",
			nameof(EsqlMetadata.Size) => "_size",
			nameof(EsqlMetadata.Source) => "_source",
			nameof(EsqlMetadata.Version) => "_version",
			nameof(EsqlMetadata.Fork) => "_fork",
			_ => null
		};

	/// <summary>
	/// Returns the <see cref="MetadataField"/> flag corresponding to <paramref name="memberName"/>
	/// (the <c>_fork</c> column has no flag and returns <see cref="MetadataField.None"/>).
	/// Returns <see langword="null"/> for unknown members.
	/// </summary>
	public static MetadataField? FlagFromMemberName(string memberName) =>
		memberName switch
		{
			nameof(EsqlMetadata.Id) => MetadataField.Id,
			nameof(EsqlMetadata.Ignored) => MetadataField.Ignored,
			nameof(EsqlMetadata.Index) => MetadataField.Index,
			nameof(EsqlMetadata.IndexMode) => MetadataField.IndexMode,
			nameof(EsqlMetadata.Score) => MetadataField.Score,
			nameof(EsqlMetadata.Size) => MetadataField.Size,
			nameof(EsqlMetadata.Source) => MetadataField.Source,
			nameof(EsqlMetadata.Version) => MetadataField.Version,
			nameof(EsqlMetadata.Fork) => MetadataField.None,
			_ => null
		};

	/// <summary>Enumerates the ES|QL identifiers corresponding to the flags set on <paramref name="metadata"/>.</summary>
	public static IEnumerable<string> EnumerateNames(MetadataField metadata)
	{
		if ((metadata & MetadataField.Id) != 0)
			yield return "_id";

		if ((metadata & MetadataField.Ignored) != 0)
			yield return "_ignored";

		if ((metadata & MetadataField.Index) != 0)
			yield return "_index";

		if ((metadata & MetadataField.IndexMode) != 0)
			yield return "_index_mode";

		if ((metadata & MetadataField.Score) != 0)
			yield return "_score";

		if ((metadata & MetadataField.Size) != 0)
			yield return "_size";

		if ((metadata & MetadataField.Source) != 0)
			yield return "_source";

		if ((metadata & MetadataField.Version) != 0)
			yield return "_version";
	}
}
