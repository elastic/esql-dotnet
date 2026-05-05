// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using static Elastic.Esql.Functions.EsqlFunctions;

namespace Elastic.Esql.Integration.Tests.Esql;

public class VectorSimilarityTests : IntegrationTestBase
{
	[Test]
	public async Task VCosine_Eval_OrdersByCosineSimilarity()
	{
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Select(b => new { b.Id, Sim = VCosine(b.TitleVec, query) })
			.OrderByDescending(r => r.Sim)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
		results[0].Id.Should().Be("book-01");
		results[0].Sim.Should().BeApproximately(1.0, 0.0001);

		// monotonically non-increasing
		for (var i = 1; i < results.Count; i++)
			results[i].Sim.Should().BeLessThanOrEqualTo(results[i - 1].Sim);
	}

	[Test]
	public async Task VDotProduct_Eval_ReturnsExpectedValueForExactMatch()
	{
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Where(b => b.Id == "book-01")
			.Select(b => new { b.Id, Sim = VDotProduct(b.TitleVec, query) })
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(1);
		// dot([1,0,0,0], [1,0,0,0]) = 1.0
		results[0].Sim.Should().BeApproximately(1.0, 0.0001);
	}

	[Test]
	public async Task VL1Norm_Eval_OrdersByManhattanDistance()
	{
		// L1 norm is a distance: ascending order = closest first.
		var query = new float[] { 1f, 0f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Select(b => new { b.Id, Dist = VL1Norm(b.TitleVec, query) })
			.OrderBy(r => r.Dist)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
		results[0].Id.Should().Be("book-01");
		results[0].Dist.Should().BeApproximately(0, 0.0001);
	}

	[Test]
	public async Task VL2Norm_Eval_OrdersByEuclideanDistance()
	{
		var query = new float[] { 0f, 1f, 0f, 0f };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Select(b => new { b.Id, Dist = VL2Norm(b.TitleVec, query) })
			.OrderBy(r => r.Dist)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
		results[0].Id.Should().Be("book-04");
		results[0].Dist.Should().BeApproximately(0, 0.0001);
	}

	[Test]
	public async Task VHamming_Byte_OrdersByHammingDistance()
	{
		// Pure red query against RGB vectors; book-01 is exact red ([-1, 0, 0] signed = [255, 0, 0] unsigned).
		var query = new float[] { -1, 0, 0 };

		var results = await Fixture.EsqlClient
			.CreateQuery<TestBook>()
			.From(TestDataSeeder.BookIndex)
			.Select(b => new { b.Id, Dist = VHamming(b.RgbVector, query) })
			.OrderBy(r => r.Dist)
			.Take(3)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(3);
		results[0].Id.Should().Be("book-01");
		results[0].Dist.Should().Be(0);
	}
}
