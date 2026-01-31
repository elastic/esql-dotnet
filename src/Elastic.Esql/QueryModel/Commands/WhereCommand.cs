// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the WHERE command.
/// </summary>
public class WhereCommand(string condition) : QueryCommand
{
	public string Condition { get; } = condition ?? throw new ArgumentNullException(nameof(condition));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
