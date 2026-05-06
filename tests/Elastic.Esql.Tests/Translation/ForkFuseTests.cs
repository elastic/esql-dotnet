// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class ForkFuseTests : EsqlTestBase
{
	[Test]
	public void Fork_TwoBranches_GeneratesParenthesisedBranches()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "Shakespeare")).Take(100),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f, 2f })).Take(100))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _id, _index, _score
			| FORK (WHERE MATCH(title, "Shakespeare") | LIMIT 100) (WHERE KNN(titleVec, [1, 2]) | LIMIT 100)
			""".NativeLineEndings());
	}

	[Test]
	public void Fuse_DefaultRrf_GeneratesBareFuse()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "shakespeare")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse()
			.ToString();

		_ = esql.Should().EndWith("| FUSE");
	}

	[Test]
	public void Fuse_RrfWithRankConstant_EmitsWithClause()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse(rankConstant: 80)
			.ToString();

		_ = esql.Should().EndWith("| FUSE WITH { \"rank_constant\": 80 }");
	}

	[Test]
	public void Fuse_LinearWithWeightsAndNormalizer_EmitsCorrectClauses()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.7, 0.3])
			.ToString();

		_ = esql.Should().EndWith("| FUSE LINEAR WITH { \"normalizer\": \"minmax\", \"weights\": { \"fork1\": 0.7, \"fork2\": 0.3 } }");
	}

	[Test]
	public void Fuse_WithMismatchedWeights_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse(weights: [0.7, 0.3, 0.5])
			.ToString();

		_ = act.Should().Throw<ArgumentException>().WithMessage("*weights count*3*2*");
	}

	[Test]
	public void Fuse_WithoutPrecedingFork_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books")
			.Fuse()
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Fork*");
	}

	[Test]
	public void Fuse_WithCustomKey_EmitsKeyByClause()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse(key: x => new { Id = EsqlMetadata.Id })
			.ToString();

		_ = esql.Should().EndWith("| FUSE KEY BY _id");
	}

	[Test]
	public void Fuse_WithComplexScoreLambda_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse(score: x => EsqlMetadata.Score * 2)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Fuse*score*single column*");
	}

	[Test]
	public void Fuse_WithoutLimitInBranch_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse()
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Fork branch 1*Take*LIMIT*");
	}

	[Test]
	public void Fuse_AfterFork_ClearsForkActiveSoSelectDoesNotRetainFork()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 1f })).Take(50))
			.Fuse()
			.Select(b => new { b.Title })
			.ToString();

		// _fork must NOT appear in the auto-retained KEEP after FUSE consumes it.
		_ = esql.Should().NotContain("_fork");
	}

	[Test]
	public void Fork_NestedInsideBranch_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Fork(
				b => b.Fork(
					inner => inner.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(10),
					inner => inner.Where(x => EsqlFunctions.Match(x.Title, "y")).Take(10)),
				b => b.Where(x => EsqlFunctions.Match(x.Title, "z")).Take(10))
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Nested 'Fork'*");
	}

	[Test]
	public void Fork_TwiceInPipeline_Throws()
	{
		var act = () => CreateQuery<BookDocument>()
			.From("books", MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "x")).Take(10),
				b => b.Where(x => EsqlFunctions.Match(x.Title, "y")).Take(10))
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "p")).Take(10),
				b => b.Where(x => EsqlFunctions.Match(x.Title, "q")).Take(10))
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Only one 'Fork'*");
	}
}
