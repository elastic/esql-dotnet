// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Functions;
using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class FunctionTests : IntegrationTestBase
{
	[Test]
	public async Task ToLower_ConvertsToLowercase()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { LowerBrand = EsqlFunctions.ToLower(p.Brand) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.LowerBrand.Should().Be(r.LowerBrand.ToLowerInvariant());
	}

	[Test]
	public async Task ToUpper_ConvertsToUppercase()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { UpperBrand = EsqlFunctions.ToUpper(p.Brand) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.UpperBrand.Should().Be(r.UpperBrand.ToUpperInvariant());
	}

	[Test]
	public async Task Length_ReturnsStringLength()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { p.Name, NameLen = EsqlFunctions.Length(p.Name) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.NameLen.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task Trim_RemovesWhitespace()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { Trimmed = EsqlFunctions.Trim(p.Name) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Trimmed.Should().NotBeNullOrEmpty();
	}

	[Test]
	public async Task Substring_ExtractsSubstring()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { Sub = EsqlFunctions.Substring(p.Name, 0, 3) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Sub.Should().HaveLength(3);
	}

	[Test]
	public async Task Concat_ConcatenatesStrings()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { FullName = EsqlFunctions.Concat(p.Brand, " - ", p.Name) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.FullName.Should().Contain(" - ");
	}

	[Test]
	public async Task Round_RoundsDoubleValue()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { p.Price, Rounded = EsqlFunctions.Round(p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Rounded.Should().Be(Math.Round(r.Price, MidpointRounding.AwayFromZero));
	}

	[Test]
	public async Task Abs_ReturnsAbsoluteValue()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { AbsPrice = EsqlFunctions.Abs(p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.AbsPrice.Should().BeGreaterThanOrEqualTo(0);
	}

	[Test]
	public async Task Ceil_ReturnsCeiling()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { p.Price, Ceiling = EsqlFunctions.Ceil(p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Ceiling.Should().BeGreaterThanOrEqualTo(r.Price);
	}

	[Test]
	public async Task Floor_ReturnsFloor()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.Select(p => new { p.Price, Floored = EsqlFunctions.Floor(p.Price) })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Floored.Should().BeLessThanOrEqualTo(r.Price);
	}

	[Test]
	public async Task Like_PatternMatch_FiltersByPattern()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => EsqlFunctions.Like(p.Name, "Product 1*"))
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();
	}

	[Test]
	public async Task Rlike_RegexMatch_FiltersByPattern()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Where(p => EsqlFunctions.Rlike(p.Name, "Product [1-3]"))
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();
	}

	[Test]
	public async Task Coalesce_ReturnsFirstNonNull()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestOrder>()
			.From(TestDataSeeder.OrderIndex)
			.Take(5)
			.Select(o => new { Ip = EsqlFunctions.Coalesce(o.ClientIp, "unknown") })
			.AsEsql()
			.ToListAsync();

		results.Should().NotBeEmpty();

		foreach (var r in results)
			r.Ip.Should().NotBeNullOrEmpty();
	}
}
