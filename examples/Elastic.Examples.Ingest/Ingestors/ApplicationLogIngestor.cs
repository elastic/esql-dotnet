// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Examples.Domain;
using Elastic.Examples.Domain.Models;
using Elastic.Examples.Ingest.Generators;
using Elastic.Examples.Ingest.Ingestors.Strategies;

namespace Elastic.Examples.Ingest.Ingestors;

/// <summary>
/// Ingestor for ApplicationLog documents to a data stream.
/// Randomly chooses between Bulk API and Elastic.Ingest channel.
/// </summary>
public class ApplicationLogIngestor : ILogIngestor
{
	private const int DocumentCount = 10000;
	private const int BatchSize = 500;

	public string EntityName => "Logs";

	public async Task<LogIngestResult> IngestAsync(
		ElasticsearchClient client,
		IReadOnlyList<string>? orderIds,
		IReadOnlyList<string>? productIds,
		IReadOnlyList<string>? customerIds,
		IngestCallbacks callbacks,
		CancellationToken ct = default)
	{
		try
		{
			callbacks.OnStatus($"Generating {DocumentCount:N0} logs...");
			var logs = ApplicationLogGenerator.Generate(orderIds, productIds, customerIds, DocumentCount);

			var useIngestChannel = Random.Shared.NextDouble() < 0.5;
			callbacks.OnInfo(useIngestChannel
				? "Using Elastic.Ingest channel"
				: "Using Elastic.Clients.Elasticsearch Bulk API");

			var (indexed, failed) = useIngestChannel
				? await DataStreamIngestStrategy.IngestViaChannelAsync(
					client, logs, ExampleElasticsearchContext.ApplicationLog.Context, BatchSize, callbacks, ct)
				: await DataStreamIngestStrategy.IngestViaBulkApiAsync(
					client, logs, ExampleElasticsearchContext.ApplicationLog.Context, BatchSize, callbacks, ct);

			callbacks.OnComplete(indexed, failed);
			return new LogIngestResult(indexed, failed);
		}
		catch (Exception ex)
		{
			callbacks.OnError(ex.Message);
			return new LogIngestResult(0, 0, ex.Message);
		}
	}
}
