// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Integration.Tests.Infrastructure;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Esql;

public class NestedObjectTests : IntegrationTestBase
{
	[Test]
	public async Task ToListAsync_WithNestedObjectFields_DeserializesAddress()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestUserProfile>()
			.From(TestDataSeeder.UserProfileIndex)
			.AsEsqlQueryable()
			.ToListAsync();

		results.Should().HaveCount(10);

		var withAddress = results.Where(u => u.Address is not null).ToList();
		withAddress.Should().NotBeEmpty();

		foreach (var user in withAddress)
		{
			user.Address!.Street.Should().NotBeNullOrEmpty();
			user.Address!.City.Should().NotBeNullOrEmpty();
			user.Address!.Country.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task ToListAsync_WithNestedObjectFields_NullAddress_StaysNull()
	{
		var results = await Fixture.EsqlClient
			.CreateQuery<TestUserProfile>()
			.From(TestDataSeeder.UserProfileIndex)
			.AsEsqlQueryable()
			.ToListAsync();

		var withoutAddress = results.Where(u => u.Address is null).ToList();
		withoutAddress.Should().NotBeEmpty();

		foreach (var user in withoutAddress)
		{
			user.UserId.Should().NotBeNullOrEmpty();
			user.Name.Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public async Task FirstAsync_WithNestedObjectFields_DeserializesCorrectly()
	{
		var result = await Fixture.EsqlClient
			.CreateQuery<TestUserProfile>()
			.From(TestDataSeeder.UserProfileIndex)
			.Where(u => u.Name == "User 1")
			.AsEsqlQueryable()
			.FirstAsync();

		result.Should().NotBeNull();
		result.UserId.Should().Be("user-0001");
		result.Address.Should().NotBeNull();
		result.Address!.City.Should().NotBeNullOrEmpty();
	}
}
