// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the STATS command.
/// </summary>
public sealed class StatsCommand(IEnumerable<string> aggregations, IEnumerable<string>? groupBy = null) : QueryCommand
{
	/// <summary>
	/// The aggregation expressions as final ES|QL text (e.g. <c>alias = FUNC(field)</c>): already
	/// escaped, may contain <c>?name</c> placeholders whose values live in <see cref="EsqlQuery.Parameters"/>.
	/// </summary>
	public IReadOnlyList<string> Aggregations { get; } = aggregations?.ToList() ?? throw new ArgumentNullException(nameof(aggregations));

	/// <summary>The BY grouping expressions as final ES|QL text (already escaped), or <see langword="null"/> when ungrouped.</summary>
	public IReadOnlyList<string>? GroupBy { get; } = groupBy?.ToList();

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
