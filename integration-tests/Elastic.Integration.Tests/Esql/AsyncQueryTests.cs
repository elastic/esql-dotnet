// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class AsyncQueryTests : IntegrationTestBase
{
	[Test]
	public async Task QueryAsync_StreamsResults()
	{
		var results = new List<TestProduct>();

		await foreach (var product in Fixture.EsqlClient.QueryAsync<TestProduct>(
						   q => q.From(TestDataSeeder.ProductIndex).Take(10)))
		{
			results.Add(product);
		}

		results.Should().HaveCount(10);
	}

	[Test]
	public async Task QueryAsync_WithProjection_StreamsResults()
	{
		var results = new List<object>();

		await foreach (var item in Fixture.EsqlClient.QueryAsync<TestProduct, object>(
						   q => q.From(TestDataSeeder.ProductIndex)
							   .Take(5)
							   .Select(p => new { p.Id, p.Name })))
		{
			results.Add(item);
		}

		results.Should().HaveCount(5);
	}

	[Test]
	public async Task QueryAsync_WithFilter_StreamsFilteredResults()
	{
		var results = new List<TestProduct>();

		await foreach (var product in Fixture.EsqlClient.QueryAsync<TestProduct>(
						   q => q.From(TestDataSeeder.ProductIndex)
							   .Where(p => p.InStock)
							   .Take(5)))
		{
			results.Add(product);
		}

		results.Should().HaveCountLessThanOrEqualTo(5);
		results.Should().AllSatisfy(p => p.InStock.Should().BeTrue());
	}

	[Test]
	public async Task QueryAsync_WithOrderBy_StreamsOrderedResults()
	{
		var results = new List<TestProduct>();

		await foreach (var product in Fixture.EsqlClient.QueryAsync<TestProduct>(
						   q => q.From(TestDataSeeder.ProductIndex)
							   .OrderByDescending(p => p.Price)
							   .Take(10)))
		{
			results.Add(product);
		}

		results.Should().HaveCount(10);
		results.Should().BeInDescendingOrder(p => p.Price);
	}

	[Test]
	public async Task QueryAsync_Events_StreamsDottedFieldTypes()
	{
		var results = new List<TestEvent>();

		await foreach (var e in Fixture.EsqlClient.QueryAsync<TestEvent>(
						   q => q.From(TestDataSeeder.EventIndex).Take(5)))
		{
			results.Add(e);
		}

		results.Should().HaveCount(5);

		foreach (var r in results)
		{
			r.Level.Should().NotBeNullOrEmpty();
			r.ServiceName.Should().NotBeNullOrEmpty();
		}
	}
}
