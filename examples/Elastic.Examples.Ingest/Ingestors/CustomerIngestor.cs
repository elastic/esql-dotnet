// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Examples.Domain.Models;
using Elastic.Examples.Ingest.Generators;
using Elastic.Examples.Ingest.Ingestors.Strategies;

namespace Elastic.Examples.Ingest.Ingestors;

/// <summary>
/// Ingestor for Customer documents to a traditional index with aliases.
/// Randomly chooses between Bulk API and Elastic.Ingest channel.
/// </summary>
public class CustomerIngestor : IIngestor<CustomerIngestResult>
{
	private const int DocumentCount = 500;
	private const int BatchSize = 500;

	public string EntityName => "Customers";

	public async Task<CustomerIngestResult> IngestAsync(
		ElasticsearchClient client,
		IngestCallbacks callbacks,
		CancellationToken ct = default)
	{
		try
		{
			callbacks.OnStatus($"Generating {DocumentCount:N0} customers...");
			var customers = CustomerGenerator.Generate(DocumentCount);
			var customerIds = customers.Select(c => c.Id).ToList();

			var useIngestChannel = Random.Shared.NextDouble() < 0.5;
			callbacks.OnInfo(useIngestChannel
				? "Using Elastic.Ingest channel"
				: "Using Elastic.Clients.Elasticsearch Bulk API");

			var (indexed, failed) = useIngestChannel
				? await IndexIngestStrategy.IngestViaChannelAsync(
					client, customers, Customer.Context, BatchSize, c => c.Id, callbacks, ct)
				: await IndexIngestStrategy.IngestViaBulkApiAsync(
					client, customers, Customer.Context, BatchSize, c => c.Id, callbacks, ct);

			callbacks.OnComplete(indexed, failed);
			return new CustomerIngestResult(indexed, failed, customerIds);
		}
		catch (Exception ex)
		{
			callbacks.OnError(ex.Message);
			return new CustomerIngestResult(0, 0, [], ex.Message);
		}
	}
}
