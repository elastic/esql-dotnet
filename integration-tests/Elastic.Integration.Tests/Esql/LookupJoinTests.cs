// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class LookupJoinTests : IntegrationTestBase
{
	// =========================================================================
	// Basic join functionality
	// =========================================================================

	[Test]
	public async Task LookupJoin_BasicJoin_EnrichesWithCategoryLabel()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(10)
			.LookupJoin<TestProduct, TestCategoryLookup, string, object>(
				TestDataSeeder.CategoryLookupIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new { p.Id, p.Name, p.CategoryId, c!.CategoryLabel }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);
	}

	[Test]
	public async Task LookupJoin_AllProductsHaveMatchingCategory()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.LookupJoin<TestProduct, TestCategoryLookup, string, object>(
				TestDataSeeder.CategoryLookupIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new { p.Id, p.CategoryId, c!.CategoryLabel, c.Region }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(100);
	}

	[Test]
	public async Task LookupJoin_WithWhereBeforeJoin()
	{
		var electronicsCount = TestDataSeeder.Products
			.Count(p => p.Category == ProductCategory.Electronics);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => p.Category == ProductCategory.Electronics)
			.LookupJoin<TestProduct, TestCategoryLookup, string, object>(
				TestDataSeeder.CategoryLookupIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new { p.Id, p.Name, p.CategoryId, c!.CategoryLabel }
			)
			.Take(10)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(Math.Min(10, electronicsCount));
	}

	// =========================================================================
	// Field name collision handling
	// Both TestProduct and TestCategoryOverlap have a "name" field.
	// =========================================================================

	[Test]
	public async Task LookupJoin_Collision_BothProjected_ReturnsCorrectValues()
	{
		var lookupMap = TestDataSeeder.CategoryOverlaps
			.ToDictionary(c => c.CategoryId, c => c.Name);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(10)
			.LookupJoin<TestProduct, TestCategoryOverlap, string, CollisionBothResult>(
				TestDataSeeder.CategoryOverlapIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new CollisionBothResult { OuterName = p.Name, InnerName = c!.Name }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(10);

		var first10 = TestDataSeeder.Products.Take(10).ToList();
		for (var i = 0; i < results.Count; i++)
		{
			var expected = first10[i];
			results[i].OuterName.Should().Be(expected.Name);
			results[i].InnerName.Should().Be(lookupMap[expected.CategoryId]);
		}
	}

	[Test]
	public async Task LookupJoin_Collision_OnlyOuterProjected_ReturnsOuterValue()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.LookupJoin<TestProduct, TestCategoryOverlap, string, CollisionOuterResult>(
				TestDataSeeder.CategoryOverlapIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new CollisionOuterResult { ProductName = p.Name, Region = c!.Region }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		var first5 = TestDataSeeder.Products.Take(5).ToList();
		for (var i = 0; i < results.Count; i++)
			results[i].ProductName.Should().Be(first5[i].Name);
	}

	[Test]
	public async Task LookupJoin_Collision_OnlyInnerProjected_ReturnsInnerValue()
	{
		var lookupMap = TestDataSeeder.CategoryOverlaps
			.ToDictionary(c => c.CategoryId, c => c.Name);

		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.LookupJoin<TestProduct, TestCategoryOverlap, string, CollisionInnerResult>(
				TestDataSeeder.CategoryOverlapIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new CollisionInnerResult { Name = c!.Name, Region = c.Region }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		var first5 = TestDataSeeder.Products.Take(5).ToList();
		for (var i = 0; i < results.Count; i++)
			results[i].Name.Should().Be(lookupMap[first5[i].CategoryId]);
	}

	[Test]
	public async Task LookupJoin_Collision_OuterKeptWithOriginalName_ReturnsOuterValue()
	{
		var results = await Fixture.EsqlClient
			.Query<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.LookupJoin<TestProduct, TestCategoryOverlap, string, CollisionOriginalNameResult>(
				TestDataSeeder.CategoryOverlapIndex,
				p => p.CategoryId,
				c => c.CategoryId,
				(p, c) => new CollisionOriginalNameResult { Name = p.Name, Region = c!.Region }
			)
			.AsEsql()
			.ToListAsync();

		results.Should().HaveCount(5);

		var first5 = TestDataSeeder.Products.Take(5).ToList();
		for (var i = 0; i < results.Count; i++)
			results[i].Name.Should().Be(first5[i].Name);
	}
}
