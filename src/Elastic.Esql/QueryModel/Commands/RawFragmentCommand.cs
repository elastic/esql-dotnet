// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents a raw ES|QL pipeline fragment appended to the query.
/// </summary>
internal sealed class RawFragmentCommand(string fragment) : QueryCommand
{
	public string Fragment { get; } = !string.IsNullOrWhiteSpace(fragment)
		? fragment
		: throw new ArgumentException("Raw ES|QL fragment must not be empty.", nameof(fragment));

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
