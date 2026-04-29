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
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f, 0.2f })).Take(100))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM books METADATA _id, _index, _score
			| FORK (WHERE MATCH(title, "Shakespeare") | LIMIT 100) (WHERE KNN(titleVec, [0.1, 0.2]) | LIMIT 100)
			""".NativeLineEndings());
	}

	[Test]
	public void Fuse_DefaultRrf_GeneratesBareFuse()
	{
		var esql = CreateQuery<BookDocument>()
			.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => EsqlFunctions.Match(x.Title, "shakespeare")).Take(50),
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f })).Take(50))
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
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f })).Take(50))
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
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f })).Take(50))
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
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f })).Take(50))
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
				b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, new float[] { 0.1f })).Take(50))
			.Fuse(key: x => new { Id = EsqlMetadata.Id })
			.ToString();

		_ = esql.Should().EndWith("| FUSE KEY BY _id");
	}
}
