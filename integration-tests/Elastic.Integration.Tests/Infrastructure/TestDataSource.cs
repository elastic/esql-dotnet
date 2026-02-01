// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Integration.Tests.Infrastructure;

/// <summary>
/// Provides shared ElasticsearchFixture instance for all tests.
/// </summary>
public static class SharedFixture
{
	private static ElasticsearchFixture? _fixture;
	private static readonly SemaphoreSlim _lock = new(1, 1);
	private static bool _initialized;

	public static async Task<ElasticsearchFixture> GetFixtureAsync()
	{
		await _lock.WaitAsync();
		try
		{
			if (_initialized && _fixture != null)
				return _fixture;

			_fixture = await ElasticsearchFixture.CreateAsync();

			if (!_fixture.DataIngested)
			{
				await IngestHelper.IngestAllTestDataAsync(_fixture);
				_fixture.MarkDataIngested();
			}

			_initialized = true;
			return _fixture;
		}
		finally
		{
			_lock.Release();
		}
	}
}

/// <summary>
/// Base class for tests that need an ElasticsearchFixture.
/// </summary>
public abstract class IntegrationTestBase
{
	protected ElasticsearchFixture Fixture { get; private set; } = null!;

	[Before(Test)]
	public async Task SetupFixture() => Fixture = await SharedFixture.GetFixtureAsync();
}
