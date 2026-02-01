// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Examples.Domain.Models;
using Elastic.Examples.Ingest.Generators;
using Elastic.Examples.Ingest.Ingestors.Strategies;

namespace Elastic.Examples.Ingest.Ingestors;

/// <summary>
/// Ingestor for Order documents to a rolling index with date pattern.
/// Randomly chooses between Bulk API and Elastic.Ingest channel.
/// </summary>
public class OrderIngestor : IOrderIngestor
{
	private const int DocumentCount = 5000;
	private const int BatchSize = 500;

	public string EntityName => "Orders";

	public async Task<OrderIngestResult> IngestAsync(
		ElasticsearchClient client,
		IReadOnlyList<string> productIds,
		IReadOnlyList<string> customerIds,
		IngestCallbacks callbacks,
		CancellationToken ct = default)
	{
		try
		{
			callbacks.OnStatus($"Generating {DocumentCount:N0} orders...");
			var orders = OrderGenerator.Generate(productIds, customerIds, DocumentCount);
			var orderIds = orders.Select(o => o.Id).ToList();

			var useIngestChannel = Random.Shared.NextDouble() < 0.5;
			callbacks.OnInfo(useIngestChannel
				? "Using Elastic.Ingest channel"
				: "Using Elastic.Clients.Elasticsearch Bulk API");

			var (indexed, failed) = useIngestChannel
				? await IndexIngestStrategy.IngestViaChannelAsync(
					client, orders, Order.Context, BatchSize, o => o.Id, callbacks, ct)
				: await IndexIngestStrategy.IngestViaBulkApiAsync(
					client, orders, Order.Context, BatchSize, o => o.Id, callbacks, ct);

			callbacks.OnComplete(indexed, failed);
			return new OrderIngestResult(indexed, failed, orderIds);
		}
		catch (Exception ex)
		{
			callbacks.OnError(ex.Message);
			return new OrderIngestResult(0, 0, [], ex.Message);
		}
	}
}
