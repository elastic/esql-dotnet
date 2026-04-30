// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class MetadataTests : IntegrationTestBase
{
	[Test]
	public async Task From_WithMetadataScore_ScoreColumnAvailableInOrderBy()
	{
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
		results[0].Id.Should().Be("book-01"); // exact-vector match should rank first
	}

	[Test]
	public async Task EsqlMetadataId_Projection_RenamesToTargetProperty()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id)
			.Where(b => b.Id == "book-01")
			.Select(b => new BookIdTitle { Id = EsqlMetadata.Id, Title = b.Title })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().NotBeNullOrEmpty();
		results[0].Title.Should().Be("Programming Patterns");
	}

	[Test]
	public async Task EsqlMetadataId_RoundTrips_AsDocumentId()
	{
		// The Elasticsearch client auto-detects the .Id property and uses its value as the
		// document _id during ingestion, so EsqlMetadata.Id should round-trip to "book-01"
		// for the corresponding document.
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id)
			.Where(b => b.Id == "book-01")
			.Select(b => new BookIdTitle { Id = EsqlMetadata.Id, Title = b.Title })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Id.Should().Be("book-01");
	}

	[Test]
	public async Task From_NoMetadata_ReferencingScore_ThrowsAtTranslationTime()
	{
		var query = new float[] { 1f, 0f, 0f, 0f };

		var act = async () =>
		{
			_ = await Fixture.EsqlClient
				.CreateQuery<TestBook>()
				.From(TestDataSeeder.BookIndex)
				.Where(b => Knn(b.TitleVec, query))
				.OrderByDescending(_ => EsqlMetadata.Score)
				.AsEsqlQueryable()
				.ToListAsync();
		};

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*EsqlMetadata.Score*not requested*");
	}

	[Test]
	public async Task Stats_AfterFromWithMetadata_ProducesAggregationResult()
	{
		// STATS clears active metadata; the aggregation result should not contain _score.
		// We test this via deserialisation: requesting an aggregation works even though
		// _score was originally requested on FROM.
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Score)
			.GroupBy(b => 1)
			.Select(g => new { Total = g.Count() })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		results[0].Total.Should().Be(TestDataSeeder.Books.Count);
	}

	[Test]
	public async Task From_WithMultipleMetadata_AutoRetainsAllInResults()
	{
		// Project `Title` only via Select; metadata fields should still survive in the response,
		// available to subsequent commands, even though they are not explicitly Selected.
		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex, MetadataField.Id | MetadataField.Index)
			.Where(b => b.Id == "book-01")
			.Select(b => new BookIdTitle { Id = EsqlMetadata.Id, Title = b.Title })
			.AsEsqlQueryable()
			.ToListAsync();

		// If auto-retain works, the projection that consumes _id via the marker still succeeds.
		results.Should().HaveCount(1);
		results[0].Id.Should().NotBeNullOrEmpty();
	}
}
