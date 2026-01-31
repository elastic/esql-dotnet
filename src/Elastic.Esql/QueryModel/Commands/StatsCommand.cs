// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the STATS command.
/// </summary>
public class StatsCommand(IEnumerable<string> aggregations, IEnumerable<string>? groupBy = null) : QueryCommand
{
	public IReadOnlyList<string> Aggregations { get; } = aggregations?.ToList() ?? throw new ArgumentNullException(nameof(aggregations));
	public IReadOnlyList<string>? GroupBy { get; } = groupBy?.ToList();

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
