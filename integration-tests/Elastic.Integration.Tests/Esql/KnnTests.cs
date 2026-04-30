// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Vectors;

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class KnnTests : IntegrationTestBase
{
	// =========================================================================
	// Float-vector KNN
	// =========================================================================

	[Test]
	public async Task Knn_Float_BasicSearch_ReturnsExactMatchFirst()
	{
		// Query [1, 0, 0, 0] should rank book-01 (vector [1, 0, 0, 0]) first.
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results[0].Id.Should().Be("book-01");
	}

	[Test]
	public async Task Knn_Float_WithLimit_HonorsK()
	{
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query))
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
	}

	[Test]
	public async Task Knn_Float_WithExplicitOptions_RespectsKAndCandidates()
	{
		var query = new float[] { 0f, 1f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query, new { k = 2, min_candidates = 50 }))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(2);

		// books 4-6 cluster around the unit-y axis; the closest should be book-04 ([0,1,0,0])
		results[0].Id.Should().Be("book-04");
	}

	[Test]
	public async Task Knn_Float_WithSimilarityThreshold_FiltersDistantVectors()
	{
		// A high similarity threshold against the unit-x query should match only the very-close vectors.
		// Note: ES|QL KNN uses the option name 'similarity', not 'similarity_threshold' (QueryDSL).
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query, new { similarity = 0.99 }))
			.AsEsqlQueryable()
			.ToListAsync();

		// Only book-01 has the exact match; book-02 / book-03 should fall under the 0.99 cosine threshold.
		results.Should().NotBeEmpty();
		results.Select(b => b.Id).Should().Contain("book-01");
		results.Select(b => b.Id).Should().NotContain("book-04"); // orthogonal axis
	}

	[Test]
	public async Task Knn_Float_WithLexicalPrefilter_NarrowsCandidateSet()
	{
		var query = new float[] { 0f, 0f, 1f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Match(b.Title, "Shakespeare"))
			.Where(b => Knn(b.TitleVec, query))
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results.Should().AllSatisfy(b => b.Title.Should().Contain("Shakespeare"));
	}

	[Test]
	public async Task Knn_Float_WithCapturedClosure_RoundtripsThroughConverter()
	{
		// The query vector is a closure-captured float[] (no explicit FloatVector cast).
		// Verifies that the closure path via TryEmitVectorConvert produces a server-acceptable payload.
		var capturedVec = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, capturedVec))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(2)
			.AsEsqlQueryable()
			.ToListAsync();

		// books 10 & 11 are at (0.5, 0.5, 0.5, 0.5) and (0.4, 0.4, 0.4, 0.4) -- they share direction
		// so cosine similarity is identical (== 1.0) and ES may return them in either order.
		results.Should().HaveCount(2);
		results.Select(b => b.Id).Should().BeEquivalentTo(["book-10", "book-11"]);
	}

	// =========================================================================
	// Byte-vector KNN
	// =========================================================================

	[Test]
	public async Task Knn_Byte_BasicSearch_ReturnsExactMatchFirst()
	{
		// Pure red byte vector should rank book-01 (RGB [255, 0, 0]) first.
		var query = new byte[] { 255, 0, 0 };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.RgbVector, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().NotBeEmpty();
		results[0].Id.Should().Be("book-01");
	}

	[Test]
	public async Task Knn_Byte_WithExplicitByteVector_ProducesIdenticalResultsAsImplicit()
	{
		var query = new ByteVector(new byte[] { 0, 255, 0 });

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.RgbVector, query))
			.OrderByDescending(_ => EsqlMetadata.Score)
			.Take(1)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().Be("book-04");
	}

	// =========================================================================
	// _score metadata
	// =========================================================================

	[Test]
	public async Task Knn_ScoreMetadata_PopulatesPositiveScore()
	{
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.Where(b => Knn(b.TitleVec, query))
			.Select(b => new BookIdScore { Id = b.Id, Score = EsqlMetadata.Score })
			.OrderByDescending(r => r.Score)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
		results.Should().AllSatisfy(r =>
		{
			r.Id.Should().NotBeNullOrEmpty();
			r.Score.Should().BeGreaterThan(0);
		});

		// scores should be monotonically non-increasing
		for (var i = 1; i < results.Count; i++)
			results[i].Score.Should().BeLessThanOrEqualTo(results[i - 1].Score);
	}
}
