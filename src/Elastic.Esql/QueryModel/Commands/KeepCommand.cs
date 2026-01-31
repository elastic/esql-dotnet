// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the KEEP command.
/// </summary>
public class KeepCommand : QueryCommand
{
	public IReadOnlyList<string> Fields { get; }

	public KeepCommand(params string[] fields) => Fields = fields ?? throw new ArgumentNullException(nameof(fields));

	public KeepCommand(IEnumerable<string> fields) => Fields = fields?.ToList() ?? throw new ArgumentNullException(nameof(fields));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
