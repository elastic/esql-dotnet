// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the LOOKUP JOIN command.
/// </summary>
public class LookupJoinCommand(string lookupIndex, string onCondition) : QueryCommand
{
	/// <summary>The lookup index name.</summary>
	public string LookupIndex { get; } = lookupIndex ?? throw new ArgumentNullException(nameof(lookupIndex));

	/// <summary>The translated ON condition (simple field names or expression-based conditions).</summary>
	public string OnCondition { get; } = onCondition ?? throw new ArgumentNullException(nameof(onCondition));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
