// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Materialization;

/// <summary>
/// A member of the result that a projection declared as read from <c>_source</c>, with
/// <c>EsqlMetadata.SourceAs(o.Lines)</c>: the member is bound to the value at <see cref="Path"/>
/// inside the document rather than to a column.
/// </summary>
/// <param name="Name">The member's name in the result row, as the columns name it; empty for the whole row.</param>
/// <param name="Path">The property names leading to the value inside <c>_source</c>; empty when the document is the row.</param>
internal sealed record SourceMember(string Name, IReadOnlyList<string> Path)
{
	/// <summary>A key that tells two declarations apart, for caching what is built from them.</summary>
	public static string Signature(IReadOnlyList<SourceMember> members) =>
		string.Join("\n", members.Select(member => member.Name + "=" + string.Join(".", member.Path)));
}

/// <summary>A declared member, as the <c>_source</c> leaf writes it into the row.</summary>
/// <param name="PrefixBytes">The member's <c>"name":</c> prefix.</param>
/// <param name="Path">The property names leading to the value inside the document.</param>
/// <param name="IsCollection">Whether the target is a collection, which a single object is wrapped into.</param>
internal sealed record SourceBinding(byte[] PrefixBytes, string[] Path, bool IsCollection)
{
	/// <summary>
	/// When the document is the row, the names of the row's collection members, so that a single object
	/// the document holds for one of them is still read as a list.
	/// </summary>
	public HashSet<string>? CollectionMembers { get; init; }
}
