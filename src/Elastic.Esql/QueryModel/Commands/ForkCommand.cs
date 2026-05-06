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
	/// <summary>The fork branches, each as an ordered list of ES|QL fragments.</summary>
	public IReadOnlyList<IReadOnlyList<string>> Branches { get; } = branches ?? throw new ArgumentNullException(nameof(branches));

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
