// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the ES|QL <c>FUSE</c> command, with all column / option overrides resolved
/// to their ES|QL identifiers / values.
/// </summary>
public sealed class FuseCommand(
	FuseMethod method,
	int? rankConstant = null,
	ScoreNormalizer normalizer = ScoreNormalizer.None,
	IReadOnlyList<double>? weights = null,
	string? scoreColumn = null,
	string? groupColumn = null,
	IReadOnlyList<string>? keyColumns = null
) : QueryCommand
{
	public FuseMethod Method { get; } = method;
	public int? RankConstant { get; } = rankConstant;
	public ScoreNormalizer Normalizer { get; } = normalizer;
	public IReadOnlyList<double>? Weights { get; } = weights?.ToList();
	public string? ScoreColumn { get; } = scoreColumn;
	public string? GroupColumn { get; } = groupColumn;
	public IReadOnlyList<string>? KeyColumns { get; } = keyColumns?.ToList();

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
