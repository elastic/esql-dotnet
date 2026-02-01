// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Examples.Domain.Models;
using Elastic.Examples.Ingest.Generators;
using Elastic.Examples.Ingest.Ingestors.Strategies;

namespace Elastic.Examples.Ingest.Ingestors;

/// <summary>
/// Ingestor for ApplicationMetric documents to a data stream.
/// Randomly chooses between Bulk API and Elastic.Ingest channel.
/// </summary>
public class ApplicationMetricIngestor : IIngestor<MetricIngestResult>
{
	private const int DocumentCount = 5000;
	private const int BatchSize = 500;

	public string EntityName => "Metrics";

	public async Task<MetricIngestResult> IngestAsync(
		ElasticsearchClient client,
		IngestCallbacks callbacks,
		CancellationToken ct = default)
	{
		try
		{
			callbacks.OnStatus($"Generating {DocumentCount:N0} metrics...");
			var metrics = ApplicationMetricGenerator.Generate(DocumentCount);

			var useIngestChannel = Random.Shared.NextDouble() < 0.5;
			callbacks.OnInfo(useIngestChannel
				? "Using Elastic.Ingest channel"
				: "Using Elastic.Clients.Elasticsearch Bulk API");

			var (indexed, failed) = useIngestChannel
				? await DataStreamIngestStrategy.IngestViaChannelAsync(
					client, metrics, ApplicationMetric.Context, BatchSize, callbacks, ct)
				: await DataStreamIngestStrategy.IngestViaBulkApiAsync(
					client, metrics, ApplicationMetric.Context, BatchSize, callbacks, ct);

			callbacks.OnComplete(indexed, failed);
			return new MetricIngestResult(indexed, failed);
		}
		catch (Exception ex)
		{
			callbacks.OnError(ex.Message);
			return new MetricIngestResult(0, 0, ex.Message);
		}
	}
}
