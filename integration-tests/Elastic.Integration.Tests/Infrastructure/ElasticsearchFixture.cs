// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Clients.Esql;
using Elastic.Transport;
using Testcontainers.Elasticsearch;

namespace Elastic.Esql.Integration.Tests.Infrastructure;

/// <summary>
/// Shared test fixture providing Elasticsearch and ES|QL clients.
/// Uses an external instance when ELASTICSEARCH_URL and ELASTICSEARCH_APIKEY env vars are set,
/// otherwise spins up an Elasticsearch Testcontainer automatically.
/// </summary>
public sealed class ElasticsearchFixture : IAsyncDisposable
{
	private const string ContainerImage = "docker.elastic.co/elasticsearch/elasticsearch:8.19.12";
	private const string ContainerPassword = "elastic-integration-tests";

	private readonly ElasticsearchContainer? _container;

	public ElasticsearchClient ElasticsearchClient { get; }

	public EsqlClient EsqlClient { get; }

	public string ElasticsearchUrl { get; }

	public bool DataIngested { get; private set; }

	private ElasticsearchFixture(
		ElasticsearchContainer? container,
		ElasticsearchClient esClient,
		EsqlClient esqlClient,
		string url)
	{
		_container = container;
		ElasticsearchClient = esClient;
		EsqlClient = esqlClient;
		ElasticsearchUrl = url;
	}

	public static async Task<ElasticsearchFixture> CreateAsync(CancellationToken ct = default)
	{
		var url = Environment.GetEnvironmentVariable("ELASTICSEARCH_URL");
		var apiKey = Environment.GetEnvironmentVariable("ELASTICSEARCH_APIKEY");

		if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(apiKey))
			return CreateFromExternalInstance(url, apiKey);

		return await CreateFromContainerAsync(ct).ConfigureAwait(false);
	}

	public void MarkDataIngested() => DataIngested = true;

	public async ValueTask DisposeAsync()
	{
		EsqlClient.Dispose();

		if (_container is not null)
			await _container.DisposeAsync().ConfigureAwait(false);
	}

	private static ElasticsearchFixture CreateFromExternalInstance(string url, string apiKey)
	{
		var nodePool = new SingleNodePool(new Uri(url));
		var esSettings = new ElasticsearchClientSettings(
			nodePool,
			sourceSerializer: (_, s) => new DefaultSourceSerializer(s, IntegrationJsonContext.Default)
		).Authentication(new ApiKey(apiKey));
		var esClient = new ElasticsearchClient(esSettings);

		var transportConfig = new TransportConfiguration(new Uri(url))
		{
			Authentication = new ApiKey(apiKey)
		};
		var transport = new DistributedTransport(transportConfig);
		var esqlClient = new EsqlClient(new EsqlClientSettings(transport) { JsonSerializerContext = IntegrationJsonContext.Default });

		return new ElasticsearchFixture(null, esClient, esqlClient, url);
	}

	private static async Task<ElasticsearchFixture> CreateFromContainerAsync(CancellationToken ct)
	{
		var container = new ElasticsearchBuilder(ContainerImage)
			.WithPassword(ContainerPassword)
			.WithEnvironment("ES_JAVA_OPTS", "-Xms1g -Xmx1g")
			.Build();

		await container.StartAsync(ct).ConfigureAwait(false);

		var connectionUri = new Uri(container.GetConnectionString());
		var baseUrl = new Uri($"{connectionUri.Scheme}://{connectionUri.Host}:{connectionUri.Port}");

		var nodePool = new SingleNodePool(baseUrl);
		var esSettings = new ElasticsearchClientSettings(
			nodePool,
			sourceSerializer: (_, s) => new DefaultSourceSerializer(s, IntegrationJsonContext.Default)
		)
			.Authentication(new BasicAuthentication("elastic", ContainerPassword))
			.ServerCertificateValidationCallback(CertificateValidations.AllowAll);
		var esClient = new ElasticsearchClient(esSettings);

		var transportConfig = new TransportConfiguration(baseUrl)
		{
			Authentication = new BasicAuthentication("elastic", ContainerPassword),
			ServerCertificateValidationCallback = CertificateValidations.AllowAll
		};
		var transport = new DistributedTransport(transportConfig);
		var esqlClient = new EsqlClient(new EsqlClientSettings(transport) { JsonSerializerContext = IntegrationJsonContext.Default });

		return new ElasticsearchFixture(container, esClient, esqlClient, baseUrl.ToString());
	}
}
