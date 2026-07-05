// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq;

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the ES|QL <c>FORK</c> command.
/// </summary>
public sealed class ForkCommand(IReadOnlyList<ForkBranch> branches) : QueryCommand
{
	/// <summary>The fork branches.</summary>
	public IReadOnlyList<ForkBranch> Branches { get; } =
		[.. (branches ?? throw new ArgumentNullException(nameof(branches)))
			.Select(b => b ?? throw new ArgumentException("Fork branches must not contain null entries.", nameof(branches)))];

	internal override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
