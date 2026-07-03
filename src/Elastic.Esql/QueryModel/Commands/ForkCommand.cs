// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the ES|QL <c>FORK</c> command. Each branch contains an ordered list of
/// already-formatted ES|QL pipeline fragments (without the leading pipe).
/// </summary>
public sealed class ForkCommand(IReadOnlyList<IReadOnlyList<string>> branches) : QueryCommand
{
	/// <summary>
	/// The fork branches, each as an ordered list of final ES|QL fragments (without the leading
	/// pipe): already escaped, may contain <c>?name</c> placeholders whose values live in
	/// <see cref="EsqlQuery.Parameters"/>.
	/// </summary>
	public IReadOnlyList<IReadOnlyList<string>> Branches { get; } =
		[.. (branches ?? throw new ArgumentNullException(nameof(branches)))
			.Select(static branch => (IReadOnlyList<string>)[.. branch])];

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
