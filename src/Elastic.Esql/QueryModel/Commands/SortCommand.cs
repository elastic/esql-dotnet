// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the SORT command.
/// </summary>
public class SortCommand : QueryCommand
{
	public IReadOnlyList<SortField> Fields { get; }

	public SortCommand(params SortField[] fields) => Fields = fields ?? throw new ArgumentNullException(nameof(fields));

	public SortCommand(IEnumerable<SortField> fields) => Fields = fields?.ToList() ?? throw new ArgumentNullException(nameof(fields));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a field in a SORT command.
/// </summary>
public class SortField(string fieldName, bool descending = false)
{
	public string FieldName { get; } = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
	public bool Descending { get; } = descending;
}
