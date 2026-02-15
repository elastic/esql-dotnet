// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the ROW source command.
/// </summary>
public class RowCommand : QueryCommand
{
	public IReadOnlyList<string> Expressions { get; }

	public RowCommand(params string[] expressions) =>
		Expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));

	public RowCommand(IEnumerable<string> expressions) =>
		Expressions = expressions?.ToList() ?? throw new ArgumentNullException(nameof(expressions));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
