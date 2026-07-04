// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// A single <c>FORK</c> branch: an ordered list of already-formatted ES|QL pipeline fragments
/// (without the leading pipe) plus structural facts recorded during translation.
/// </summary>
public sealed class ForkBranch(IReadOnlyList<string> fragments, bool hasLimit)
{
	/// <summary>The branch pipeline as an ordered list of ES|QL fragments.</summary>
	public IReadOnlyList<string> Fragments { get; } = [.. fragments ?? throw new ArgumentNullException(nameof(fragments))];

	/// <summary>True when the branch pipeline contains a LIMIT. FUSE requires a LIMIT in every preceding FORK branch.</summary>
	public bool HasLimit { get; } = hasLimit;
}
