// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the KEEP command.
/// </summary>
public sealed class KeepCommand : QueryCommand
{
	/// <summary>The field names to keep, as final ES|QL identifiers (already escaped and qualified).</summary>
	public IReadOnlyList<string> Fields { get; }

	public KeepCommand(params string[] fields) =>
		Fields = [.. fields ?? throw new ArgumentNullException(nameof(fields))];

	public KeepCommand(IEnumerable<string> fields) =>
		Fields = fields?.ToList() ?? throw new ArgumentNullException(nameof(fields));

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
