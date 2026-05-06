// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Runs up to 8 parallel pipeline branches over the same input data and merges the
	/// results, adding a <c>_fork</c> discriminator column with values <c>fork1</c>,
	/// <c>fork2</c>, ... per branch declaration order.
	/// </summary>
	public static IQueryable<TSource> Fork<TSource>(
		this IQueryable<TSource> source,
		params Expression<Func<IQueryable<TSource>, IQueryable<TSource>>>[] branches)
	{
		Verify.NotNull(source);
		Verify.NotNull(branches);

		if (branches.Length == 0)
			throw new ArgumentException("FORK requires at least one branch.", nameof(branches));

		if (branches.Length > 8)
			throw new ArgumentException("FORK supports at most 8 branches.", nameof(branches));

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<IQueryable<TSource>, IQueryable<TSource>>>[], IQueryable<TSource>>(Fork).Method,
			Expression.Constant(branches)
		);
	}

	/// <summary>
	/// Merges the result rows produced by a preceding <c>FORK</c> command using Reciprocal Rank Fusion (default).
	/// </summary>
	public static IQueryable<TSource> Fuse<TSource>(this IQueryable<TSource> source) =>
		Fuse(source, FuseMethod.Rrf, rankConstant: null, normalizer: ScoreNormalizer.None, weights: null, score: null, group: null, key: null);

	/// <summary>
	/// Merges the result rows produced by a preceding <c>FORK</c> command, optionally overriding
	/// the merging method, scoring options, weights, and column references.
	/// </summary>
	/// <param name="source">The query source (must follow a <c>Fork</c> command).</param>
	/// <param name="method">Merging algorithm. Defaults to <see cref="FuseMethod.Rrf"/>.</param>
	/// <param name="rankConstant">RRF rank constant (<c>k</c>). Defaults to <c>60</c> when omitted.</param>
	/// <param name="normalizer">Score normalization (linear method only).</param>
	/// <param name="weights">Per-branch weights aligned to <c>FORK</c> declaration order. Length must equal the number of branches.</param>
	/// <param name="score">Lambda selecting the score column (defaults to <c>_score</c>).</param>
	/// <param name="group">Lambda selecting the group column (defaults to <c>_fork</c>).</param>
	/// <param name="key">Lambda selecting one or more key columns (defaults to <c>_id, _index</c>). Use <c>x =&gt; new { x.Id, x.Index }</c> for composite keys.</param>
	public static IQueryable<TSource> Fuse<TSource>(
		this IQueryable<TSource> source,
		FuseMethod method = FuseMethod.Rrf,
		int? rankConstant = null,
		ScoreNormalizer normalizer = ScoreNormalizer.None,
		double[]? weights = null,
		Expression<Func<TSource, object?>>? score = null,
		Expression<Func<TSource, object?>>? group = null,
		Expression<Func<TSource, object?>>? key = null)
	{
		Verify.NotNull(source);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, FuseMethod, int?, ScoreNormalizer, double[]?, Expression<Func<TSource, object?>>?, Expression<Func<TSource, object?>>?, Expression<Func<TSource, object?>>?, IQueryable<TSource>>(Fuse).Method,
			Expression.Constant(method),
			Expression.Constant(rankConstant, typeof(int?)),
			Expression.Constant(normalizer),
			Expression.Constant(weights, typeof(double[])),
			Expression.Constant(score, typeof(Expression<Func<TSource, object?>>)),
			Expression.Constant(group, typeof(Expression<Func<TSource, object?>>)),
			Expression.Constant(key, typeof(Expression<Func<TSource, object?>>))
		);
	}
}
