// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Examples.Ingest.Channels;
using Elastic.Examples.Ingest.Ingestors;
using Elastic.Ingest.Elasticsearch;
using Elastic.Channels;

namespace Elastic.Integration.Tests.Infrastructure;

/// <summary>Helper for one-time test data ingestion using Elastic.Ingest channels.</summary>
public static class IngestHelper
{
	private const int ProductCount = 1000;
	private const int CustomerCount = 500;
	private const int OrderCount = 5000;
	private const int LogCount = 10000;
	private const int MetricCount = 5000;
	private const int BatchSize = 500;

	/// <summary>Ingests all test data to Elasticsearch using MappingIndexChannel and MappingDataStreamChannel.</summary>
	public static async Task IngestAllTestDataAsync(ElasticsearchFixture fixture, CancellationToken ct = default)
	{
		var callbacks = CreateCallbacks();

		// Generate data with seed 12345 (built into generators)
		var products = ProductGenerator.Generate(ProductCount);
		var customers = CustomerGenerator.Generate(CustomerCount);
		var productIds = products.Select(p => p.Id).ToList();
		var customerIds = customers.Select(c => c.Id).ToList();
		var orders = OrderGenerator.Generate(productIds, customerIds, OrderCount);
		var orderIds = orders.Select(o => o.Id).ToList();
		var logs = ApplicationLogGenerator.Generate(orderIds, productIds, customerIds, LogCount);
		var metrics = ApplicationMetricGenerator.Generate(MetricCount);

		// Ingest products via MappingIndexChannel
		await IngestViaIndexChannelAsync(fixture.ElasticsearchClient, products, Product.Context, p => p.Id, callbacks, ct);

		// Ingest customers via MappingIndexChannel
		await IngestViaIndexChannelAsync(fixture.ElasticsearchClient, customers, Customer.Context, c => c.Id, callbacks, ct);

		// Ingest orders via MappingIndexChannel
		await IngestViaIndexChannelAsync(fixture.ElasticsearchClient, orders, Order.Context, o => o.Id, callbacks, ct);

		// Ingest logs via MappingDataStreamChannel
		await IngestViaDataStreamChannelAsync(fixture.ElasticsearchClient, logs, ApplicationLog.Context, callbacks, ct);

		// Ingest metrics via MappingDataStreamChannel
		await IngestViaDataStreamChannelAsync(fixture.ElasticsearchClient, metrics, ApplicationMetric.Context, callbacks, ct);

		// Refresh all indices to ensure data is searchable
		await fixture.ElasticsearchClient.Indices.RefreshAsync("*", ct);
	}

	private static async Task IngestViaIndexChannelAsync<T>(
		ElasticsearchClient client,
		IReadOnlyList<T> documents,
		Elastic.Mapping.ElasticsearchTypeContext context,
		Func<T, string> idLookup,
		IngestCallbacks callbacks,
		CancellationToken ct) where T : class
	{
		var options = new MappingIndexChannelOptions<T>(client)
		{
			Context = context,
			BulkOperationIdLookup = idLookup,
			OnBootstrapStatus = callbacks.OnStatus,
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = BatchSize },
			Settings = callbacks.SettingsModifier
		};

		var channel = new MappingIndexChannel<T>(options);
		try
		{
			_ = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, null, ct);

			foreach (var document in documents)
				channel.TryWrite(document);

			_ = await channel.WaitForDrainAsync(null, ct);
			_ = await channel.RefreshAsync(ct);
		}
		finally
		{
			_ = channel.TryComplete();
		}
	}

	private static async Task IngestViaDataStreamChannelAsync<T>(
		ElasticsearchClient client,
		IReadOnlyList<T> documents,
		Elastic.Mapping.ElasticsearchTypeContext context,
		IngestCallbacks callbacks,
		Cancel ct
	) where T : class
	{
		var options = new MappingDataStreamChannelOptions<T>(client)
		{
			Context = context,
			OnBootstrapStatus = callbacks.OnStatus,
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = BatchSize },
			Settings = callbacks.SettingsModifier
		};

		var channel = new MappingDataStreamChannel<T>(options);
		try
		{
			_ = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, null, ct);

			foreach (var document in documents)
				channel.TryWrite(document);

			_ = await channel.WaitForDrainAsync(null, ct);
			_ = await channel.RefreshAsync(ct);
		}
		finally
		{
			_ = channel.TryComplete();
		}
	}

	private static IngestCallbacks CreateCallbacks() =>
		new(
			OnStatus: _ => { },
			OnInfo: _ => { },
			OnProgress: (_, _) => { },
			OnComplete: (_, _) => { },
			OnError: _ => { },
			SettingsModifier: null
		);
}
