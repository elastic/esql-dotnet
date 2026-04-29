// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>The merging algorithm used by the ES|QL <c>FUSE</c> command.</summary>
public enum FuseMethod
{
	/// <summary>Reciprocal Rank Fusion (default). Combines result sets without score tuning.</summary>
	Rrf,

	/// <summary>Linear combination of (optionally normalized) scores.</summary>
	Linear
}

/// <summary>Score normalization applied by <c>FUSE LINEAR</c> before combining scores.</summary>
public enum ScoreNormalizer
{
	/// <summary>No normalization (raw scores combined as-is).</summary>
	None,

	/// <summary>Min-max normalization to <c>[0, 1]</c> per fork branch.</summary>
	MinMax
}
