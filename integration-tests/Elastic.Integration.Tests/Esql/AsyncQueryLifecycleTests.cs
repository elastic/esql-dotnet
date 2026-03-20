// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Execution;

namespace Elastic.Esql.Integration.Tests.Esql;

public class AsyncQueryLifecycleTests : IntegrationTestBase
{
	[Test]
	public async Task SubmitAsyncQueryAsync_CompletesAndReturnsRows()
	{
		await using var asyncQuery = await Fixture.EsqlClient
			.SubmitAsyncQueryAsync<TestProduct>(
				q => q.From(TestDataSeeder.ProductIndex).Take(10)
			);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(10);
	}

	[Test]
	public void SubmitAsyncQuery_Sync_CompletesAndReturnsRows()
	{
		using var asyncQuery = Fixture.EsqlClient
			.SubmitAsyncQuery<TestProduct>(
				q => q.From(TestDataSeeder.ProductIndex).Take(10)
			);

		var results = asyncQuery.ToList();

		results.Should().HaveCount(10);
	}

	[Test]
	public async Task SubmitAsyncQueryAsync_WithProjection_ReturnsProjectedRows()
	{
		await using var asyncQuery = await Fixture.EsqlClient
			.SubmitAsyncQueryAsync<TestProduct, object>(
				q => q.From(TestDataSeeder.ProductIndex)
					.Take(5)
					.Select(p => new { p.Id, p.Name })
			);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task KeepOnCompletion_True_QueryIdIsNotNull()
	{
		var options = new EsqlAsyncQueryOptions { KeepOnCompletion = true };

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		await asyncQuery.WaitForCompletionAsync();

		asyncQuery.QueryId.Should().NotBeNull();
		asyncQuery.IsCompleted.Should().BeTrue();
	}

	[Test]
	public async Task FastQuery_AsEnumerable_WaitsAndReturnsRows()
	{
		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(1)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync();

		var results = asyncQuery.AsEnumerable().ToList();

		asyncQuery.IsCompleted.Should().BeTrue();
		results.Should().HaveCount(1);
	}

	[Test]
	public async Task WaitForCompletionAsync_ThenEnumerate_ReturnsRows()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		await asyncQuery.WaitForCompletionAsync();

		asyncQuery.IsCompleted.Should().BeTrue();

		var results = new List<TestProduct>();
		await foreach (var item in asyncQuery.AsAsyncEnumerable())
			results.Add(item);

		results.Should().HaveCount(5);
	}

	[Test]
	public void WaitForCompletion_Sync_ThenEnumerate_ReturnsRows()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		using var asyncQuery = Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQuery(options);

		asyncQuery.WaitForCompletion();

		asyncQuery.IsCompleted.Should().BeTrue();

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task ZeroTimeout_KeepOnCompletion_ForcesAsyncLifecycle()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(10)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		asyncQuery.QueryId.Should().NotBeNull();

		var results = asyncQuery.AsEnumerable().ToList();

		asyncQuery.IsCompleted.Should().BeTrue();
		results.Should().HaveCount(10);
	}

	[Test]
	public async Task RefreshAsync_UpdatesState()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		asyncQuery.QueryId.Should().NotBeNull();

		// Poll until completed
		while (!asyncQuery.IsCompleted)
			await asyncQuery.RefreshAsync();

		asyncQuery.IsCompleted.Should().BeTrue();
	}

	[Test]
	public async Task KeepAlive_AcceptedByServer()
	{
		var options = new EsqlAsyncQueryOptions
		{
			KeepAlive = TimeSpan.FromMinutes(1),
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task KeepAlive_WithNonWholeHours_AcceptedByServer()
	{
		var options = new EsqlAsyncQueryOptions
		{
			KeepAlive = TimeSpan.FromMinutes(90),
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task WaitForCompletionTimeout_WithFractionalSeconds_AcceptedByServer()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.FromMilliseconds(1500),
			KeepOnCompletion = true
		};

		await using var asyncQuery = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(options);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task SubmitAsyncQueryAsync_WithFilter_ReturnsFilteredRows()
	{
		var expected = TestDataSeeder.Products.Count(p => p.InStock);

		await using var asyncQuery = await Fixture.EsqlClient
			.SubmitAsyncQueryAsync<TestProduct>(
				q => q.From(TestDataSeeder.ProductIndex).Where(p => p.InStock)
			);

		var results = asyncQuery.AsEnumerable().ToList();

		results.Should().HaveCount(expected);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}
}
