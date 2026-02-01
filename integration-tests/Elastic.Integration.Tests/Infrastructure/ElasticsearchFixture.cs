// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Elastic.Transport;

namespace Elastic.Integration.Tests.Infrastructure;

/// <summary>Shared test fixture providing Elasticsearch and ES|QL clients via Aspire.</summary>
public sealed class ElasticsearchFixture : IAsyncDisposable
{
	private readonly DistributedApplication _app;

	/// <summary>Elasticsearch high-level client for index operations and mapping verification.</summary>
	public ElasticsearchClient ElasticsearchClient { get; }

	/// <summary>ES|QL client for LINQ-based queries.</summary>
	public EsqlClient EsqlClient { get; }

	/// <summary>Elasticsearch URL from Aspire configuration.</summary>
	public string ElasticsearchUrl { get; }

	/// <summary>Indicates whether test data has been ingested.</summary>
	public bool DataIngested { get; private set; }

	private ElasticsearchFixture(DistributedApplication app, string url, string apiKey)
	{
		_app = app;
		ElasticsearchUrl = url;

		var settings = new ElasticsearchClientSettings(new Uri(url))
			.Authentication(new ApiKey(apiKey));
		ElasticsearchClient = new ElasticsearchClient(settings);

		var transportConfig = new TransportConfiguration(new Uri(url), new ApiKey(apiKey));
		var transport = new DistributedTransport(transportConfig);
		var esqlSettings = new EsqlClientSettings(transport, new Uri(url));
		EsqlClient = new EsqlClient(esqlSettings);
	}

	/// <summary>Creates the fixture by launching the Aspire test app.</summary>
	public static async Task<ElasticsearchFixture> CreateAsync(CancellationToken ct = default)
	{
		var appHost = await DistributedApplicationTestingBuilder
			.CreateAsync<Projects.aspire>(ct);

		var app = await appHost.BuildAsync(ct)
			.WaitAsync(TimeSpan.FromSeconds(60), ct);

		await app.StartAsync(ct)
			.WaitAsync(TimeSpan.FromSeconds(60), ct);

		var url = appHost.Configuration["Parameters:ElasticsearchUrl"]
			?? throw new InvalidOperationException("ElasticsearchUrl parameter not found in Aspire configuration");
		var apiKey = appHost.Configuration["Parameters:ElasticsearchApiKey"]
			?? throw new InvalidOperationException("ElasticsearchApiKey parameter not found in Aspire configuration");

		return new ElasticsearchFixture(app, url, apiKey);
	}

	/// <summary>Marks data as ingested after one-time setup.</summary>
	public void MarkDataIngested() => DataIngested = true;

	public async ValueTask DisposeAsync()
	{
		EsqlClient.Dispose();
		await _app.DisposeAsync();
	}
}
