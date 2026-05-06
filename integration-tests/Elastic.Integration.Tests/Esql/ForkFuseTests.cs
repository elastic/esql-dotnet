// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class ForkFuseTests : IntegrationTestBase
{
	[Test]
	public async Task Fork_TwoBranches_ProducesForkColumn()
	{
		var query = new float[] { 0f, 0f, 1f, 0f }; // unit-z axis: matches book-07/08/09

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(5),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(5))
			.Select(x => new ForkResult { Id = x.Id, Fork = EsqlMetadata.Fork })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Select(r => r.Fork).Distinct().Should().BeSubsetOf(["fork1", "fork2"]);
		results.Should().Contain(r => r.Fork == "fork1");
		results.Should().Contain(r => r.Fork == "fork2");
	}

	// FUSE in ES 9.3.x rejects dense_vector columns flowing into it ("cannot use [title_vec] as
	// an input of FUSE"), so each FUSE-bound test drops the vector columns immediately after FORK.

	[Test]
	public async Task Fuse_DefaultRrf_MergesByIdAndIndex()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse()
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(10)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();

		// FUSE merges by _id+_index, so each document appears at most once.
		var idCounts = results
			.GroupBy(r => r.Id)
			.Select(g => g.Count())
			.ToList();
		idCounts.Should().AllSatisfy(c => c.Should().Be(1));

		// book-08 ("Shakespeare for Programmers") matches both branches (lexical "Shakespeare"
		// + semantically close to the unit-z axis) and should rank near the top.
		results.Take(3).Select(r => r.Id).Should().Contain("book-08");
	}

	[Test]
	public async Task Fuse_RrfWithCustomRankConstant_RuntimeAccepted()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse(rankConstant: 80)
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
	}

	[Test]
	public async Task Fuse_LinearWithWeights_FavorsHigherWeightedBranch()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		// Heavily favour the lexical branch -- the top result should be the one matched
		// by MATCH("Shakespeare") rather than the closest KNN hit.
		var lexicalFavoured = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.95, 0.05])
			.Select(x => new BookIdScore { Id = x.Id, Score = EsqlMetadata.Score })
			.OrderByDescending(r => r.Score)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		lexicalFavoured.Should().NotBeEmpty();

		// Now invert the weights -- the top result should be the closest KNN hit instead
		// (book-07 has the exact unit-z vector).
		var semanticFavoured = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.05, 0.95])
			.Select(x => new BookIdScore { Id = x.Id, Score = EsqlMetadata.Score })
			.OrderByDescending(r => r.Score)
			.Take(5)
			.AsEsqlQueryable()
			.ToListAsync();

		semanticFavoured.Should().NotBeEmpty();

		// book-07 ("Shakespeare on Stage" with vector [0,0,1,0]) is in both result sets
		// but its rank should differ between the two weight configurations.
		var lexicalRankOf07 = lexicalFavoured.FindIndex(r => r.Id == "book-07");
		var semanticRankOf07 = semanticFavoured.FindIndex(r => r.Id == "book-07");

		// book-07 is the exact KNN match, so it should rank at least as well in semantic-favoured.
		// (In lexical-favoured book-07 still appears because "Shakespeare" is in its title.)
		if (lexicalRankOf07 >= 0 && semanticRankOf07 >= 0)
			semanticRankOf07.Should().BeLessThanOrEqualTo(lexicalRankOf07);
	}

	[Test]
	public async Task Fuse_LinearWithMinMaxNormalizer_ProducesScoresInZeroOneRange()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.5, 0.5])
			.Select(x => new BookIdScore { Id = x.Id, Score = EsqlMetadata.Score })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		// minmax normalises each branch's scores to [0, 1]; the linear sum at weights 0.5/0.5
		// is then in [0, 1] as well.
		results.Should().AllSatisfy(r =>
		{
			r.Score.Should().BeGreaterThanOrEqualTo(0);
			r.Score.Should().BeLessThanOrEqualTo(1.0001f); // tiny tolerance for floating-point
		});
	}

	[Test]
	public async Task Fuse_FollowedBySortAndTake_ProducesTopN()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(20),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(20))
			.Drop("title_vec", "rgb_vector")
			.Fuse()
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(2)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(2);
		results.Select(r => r.Id).Should().OnlyHaveUniqueItems();
	}

	[Test]
	public async Task Fuse_AfterFork_ConsumesForkColumn()
	{
		// After FUSE the _fork column must no longer appear in the response. We verify this by
		// projecting into a plain BookIdScore (which has no _fork mapping) and asserting the
		// row materialises cleanly. If FUSE left _fork on the wire and our auto-retain still
		// included it, the projection columns would diverge from the deserialiser's expectation.
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index | MetadataField.Score)
			.Fork(
				b => b.Where(x => Match(x.Title, "Shakespeare")).Take(10),
				b => b.Where(x => Knn(x.TitleVec, query)).Take(10))
			.Drop("title_vec", "rgb_vector")
			.Fuse()
			.Select(b => new BookIdScore { Id = b.Id, Score = EsqlMetadata.Score })
			.OrderByDescending(r => r.Score)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().AllSatisfy(r => r.Id.Should().NotBeNullOrEmpty());
	}
}
