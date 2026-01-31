// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the FROM command.
/// </summary>
public class FromCommand(string indexPattern) : QueryCommand
{
	public string IndexPattern { get; } = indexPattern ?? throw new ArgumentNullException(nameof(indexPattern));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
