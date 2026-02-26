// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the RENAME command.
/// </summary>
public class RenameCommand(IEnumerable<(string OldName, string NewName)> fields) : QueryCommand
{
	public IReadOnlyList<(string OldName, string NewName)> Fields { get; } =
		fields?.ToList() ?? throw new ArgumentNullException(nameof(fields));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
