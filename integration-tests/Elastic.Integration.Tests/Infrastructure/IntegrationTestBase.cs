// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Integration.Tests.Infrastructure;

public static class SharedFixture
{
	private static ElasticsearchFixture? Fixture;
	private static readonly SemaphoreSlim Lock = new(1, 1);
	private static bool Initialized;

	public static async Task<ElasticsearchFixture> GetFixtureAsync()
	{
		await Lock.WaitAsync();
		try
		{
			if (Initialized && Fixture != null)
				return Fixture;

			Fixture = await ElasticsearchFixture.CreateAsync();

			if (!Fixture.DataIngested)
			{
				await TestDataSeeder.SeedAllAsync(Fixture.ElasticsearchClient);
				Fixture.MarkDataIngested();
			}

			Initialized = true;
			return Fixture;
		}
		finally
		{
			Lock.Release();
		}
	}
}

public abstract class IntegrationTestBase
{
	protected ElasticsearchFixture Fixture { get; private set; } = null!;

	[Before(Test)]
	public async Task SetupFixture() => Fixture = await SharedFixture.GetFixtureAsync();
}
