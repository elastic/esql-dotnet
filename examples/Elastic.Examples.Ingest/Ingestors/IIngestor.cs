// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;

namespace Elastic.Examples.Ingest.Ingestors;

/// <summary>
/// Result of an ingestion operation.
/// </summary>
/// <param name="Indexed">Number of documents successfully indexed.</param>
/// <param name="Failed">Number of documents that failed to index.</param>
/// <param name="Error">Error message if the entire operation failed.</param>
public record IngestResult(int Indexed, int Failed, string? Error = null)
{
	/// <summary>Total documents processed.</summary>
	public int Total => Indexed + Failed;

	/// <summary>Whether the ingestion was successful (no fatal errors).</summary>
	public bool IsSuccess => Error == null;
}

/// <summary>
/// Result of product ingestion including generated product IDs.
/// </summary>
public record ProductIngestResult(int Indexed, int Failed, IReadOnlyList<string> ProductIds, string? Error = null)
	: IngestResult(Indexed, Failed, Error);

/// <summary>
/// Result of customer ingestion including generated customer IDs.
/// </summary>
public record CustomerIngestResult(int Indexed, int Failed, IReadOnlyList<string> CustomerIds, string? Error = null)
	: IngestResult(Indexed, Failed, Error);

/// <summary>
/// Result of order ingestion including generated order IDs.
/// </summary>
public record OrderIngestResult(int Indexed, int Failed, IReadOnlyList<string> OrderIds, string? Error = null)
	: IngestResult(Indexed, Failed, Error);

/// <summary>
/// Result of log ingestion.
/// </summary>
public record LogIngestResult(int Indexed, int Failed, string? Error = null)
	: IngestResult(Indexed, Failed, Error);

/// <summary>
/// Result of metric ingestion.
/// </summary>
public record MetricIngestResult(int Indexed, int Failed, string? Error = null)
	: IngestResult(Indexed, Failed, Error);

/// <summary>
/// Callbacks for reporting ingestion progress and status.
/// Allows the caller (Program.cs) to own all terminal output.
/// </summary>
/// <param name="OnStatus">Called for status messages (e.g., "Creating component template...").</param>
/// <param name="OnInfo">Called for informational messages (e.g., "Using Elastic.Ingest channel").</param>
/// <param name="OnProgress">Called with (current, total) batch progress.</param>
/// <param name="OnComplete">Called with (indexed, failed) final counts.</param>
/// <param name="OnError">Called for error messages.</param>
/// <param name="SettingsModifier">Optional function to modify settings JSON (e.g., for serverless compatibility).</param>
public record IngestCallbacks(
	Action<string> OnStatus,
	Action<string> OnInfo,
	Action<int, int> OnProgress,
	Action<int, int> OnComplete,
	Action<string> OnError,
	Func<string, string>? SettingsModifier = null
);

/// <summary>
/// Base interface for all ingestors.
/// </summary>
public interface IIngestor
{
	/// <summary>Human-readable name of the entity being ingested.</summary>
	string EntityName { get; }
}

/// <summary>
/// Interface for ingestors that don't require dependencies from other ingestors.
/// </summary>
/// <typeparam name="TResult">The result type for this ingestor.</typeparam>
public interface IIngestor<TResult> : IIngestor where TResult : IngestResult
{
	/// <summary>
	/// Ingests documents to Elasticsearch.
	/// </summary>
	/// <param name="client">The Elasticsearch client.</param>
	/// <param name="callbacks">Callbacks for reporting progress.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The ingestion result.</returns>
	Task<TResult> IngestAsync(
		ElasticsearchClient client,
		IngestCallbacks callbacks,
		CancellationToken ct = default
	);
}

/// <summary>
/// Interface for order ingestor that requires product and customer IDs.
/// </summary>
public interface IOrderIngestor : IIngestor
{
	/// <summary>
	/// Ingests orders to Elasticsearch.
	/// </summary>
	/// <param name="client">The Elasticsearch client.</param>
	/// <param name="productIds">Product IDs from prior ingestion.</param>
	/// <param name="customerIds">Customer IDs from prior ingestion.</param>
	/// <param name="callbacks">Callbacks for reporting progress.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The ingestion result.</returns>
	Task<OrderIngestResult> IngestAsync(
		ElasticsearchClient client,
		IReadOnlyList<string> productIds,
		IReadOnlyList<string> customerIds,
		IngestCallbacks callbacks,
		CancellationToken ct = default
	);
}

/// <summary>
/// Interface for log ingestor that can use context from other ingestors.
/// </summary>
public interface ILogIngestor : IIngestor
{
	/// <summary>
	/// Ingests logs to Elasticsearch.
	/// </summary>
	/// <param name="client">The Elasticsearch client.</param>
	/// <param name="orderIds">Order IDs from prior ingestion (optional).</param>
	/// <param name="productIds">Product IDs from prior ingestion (optional).</param>
	/// <param name="customerIds">Customer IDs from prior ingestion (optional).</param>
	/// <param name="callbacks">Callbacks for reporting progress.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The ingestion result.</returns>
	Task<LogIngestResult> IngestAsync(
		ElasticsearchClient client,
		IReadOnlyList<string>? orderIds,
		IReadOnlyList<string>? productIds,
		IReadOnlyList<string>? customerIds,
		IngestCallbacks callbacks,
		CancellationToken ct = default
	);
}
