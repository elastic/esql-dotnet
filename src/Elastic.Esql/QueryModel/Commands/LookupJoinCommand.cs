// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the LOOKUP JOIN command.
/// </summary>
public sealed class LookupJoinCommand(string lookupIndex, string onCondition) : QueryCommand
{
	/// <summary>The lookup index name.</summary>
	public string LookupIndex { get; } = lookupIndex ?? throw new ArgumentNullException(nameof(lookupIndex));

	/// <summary>
	/// The ON condition as final ES|QL text (simple field names or expression-based conditions):
	/// already escaped, may contain <c>?name</c> placeholders whose values live in <see cref="EsqlQuery.Parameters"/>.
	/// </summary>
	public string OnCondition { get; } = onCondition ?? throw new ArgumentNullException(nameof(onCondition));

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
