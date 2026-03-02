// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Materialization;

namespace Elastic.Clients.Esql.Execution;

/// <summary>
/// Represents an async ES|QL query that auto-cleans up on disposal.
/// Implements IAsyncDisposable to automatically DELETE the async query.
/// </summary>
public sealed class EsqlAsyncQuery<T> : IAsyncDisposable
{
	private readonly EsqlTransportExecutor _executor;
	private readonly JsonSerializerOptions _jsonOptions;
	private bool _disposed;

	internal EsqlAsyncQuery(EsqlTransportExecutor executor, EsqlResponse response, JsonSerializerOptions? jsonOptions = null)
	{
		_executor = executor;
		Response = response;
		_jsonOptions = jsonOptions ?? JsonSerializerOptions.Default;
		QueryId = response.Id;
	}

	/// <summary>The async query ID (null if completed synchronously).</summary>
	public string? QueryId { get; }

	/// <summary>Whether the query is still running.</summary>
	public bool IsRunning => Response.IsRunning;

	/// <summary>Whether results are immediately available.</summary>
	public bool IsCompleted => !IsRunning;

	/// <summary>Gets the initial response (may be partial if still running).</summary>
	public EsqlResponse Response { get; }

	/// <summary>Waits for completion and returns materialized results.</summary>
	public async Task<List<T>> ToListAsync(CancellationToken ct = default)
	{
		var finalResponse = IsRunning && QueryId != null
			? await WaitForCompletionAsync(ct)
			: Response;

		using var stream = new MemoryStream();
		await JsonSerializer.SerializeAsync(stream, finalResponse, _jsonOptions, ct).ConfigureAwait(false);
		stream.Position = 0;

		var list = new List<T>();
		await foreach (var item in EsqlResponseReader.ReadRowsAsync<T>(stream, _jsonOptions, ct).ConfigureAwait(false))
			list.Add(item);

		return list;
	}

	/// <summary>Polls until query completes.</summary>
	public async Task<EsqlResponse> WaitForCompletionAsync(CancellationToken ct = default)
	{
		if (QueryId == null || !IsRunning)
			return Response;

		EsqlResponse result;
		do
		{
			await Task.Delay(100, ct).ConfigureAwait(false);
			result = await _executor.GetAsyncStatusAsync(QueryId, ct).ConfigureAwait(false);
		} while (result.IsRunning);

		return result;
	}

	/// <summary>Disposes and DELETEs the async query from the cluster.</summary>
	public async ValueTask DisposeAsync()
	{
		if (_disposed || QueryId == null)
			return;

		_disposed = true;
		try
		{
			await _executor.DeleteAsyncQueryAsync(QueryId).ConfigureAwait(false);
		}
		catch
		{
			// Best effort cleanup
		}
	}
}
