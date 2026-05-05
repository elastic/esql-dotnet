// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Functions;

/// <summary>
/// Optional KNN parameters as a typed record. Set the properties you want to override; unset
/// properties are omitted from the generated ES|QL named-parameter object.
/// </summary>
/// <remarks>
/// Each property maps to its snake_case ES|QL counterpart (e.g. <see cref="MinCandidates"/>
/// renders as <c>min_candidates</c>). See the ES|QL <c>KNN</c> function reference for accepted
/// values.
/// </remarks>
public sealed record KnnOptions
{
	/// <summary>The number of nearest neighbours to return per shard. ES|QL: <c>k</c>.</summary>
	public int? K { get; init; }

	/// <summary>The minimum number of candidates considered during the approximate search. ES|QL: <c>min_candidates</c>.</summary>
	public int? MinCandidates { get; init; }

	/// <summary>Minimum similarity threshold for matches. ES|QL: <c>similarity</c>.</summary>
	public double? Similarity { get; init; }

	/// <summary>Score boost applied to the KNN clause. ES|QL: <c>boost</c>.</summary>
	public double? Boost { get; init; }

	/// <summary>Fraction of vectors visited during search. ES|QL: <c>visit_percentage</c>.</summary>
	public double? VisitPercentage { get; init; }

	/// <summary>Oversampling factor used when re-scoring candidates. ES|QL: <c>rescore_oversample</c>.</summary>
	public double? RescoreOversample { get; init; }
}
