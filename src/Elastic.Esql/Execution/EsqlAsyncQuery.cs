// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Execution;

/// <summary>
/// Represents an async ES|QL query that auto-cleans up on disposal.
/// Implements IAsyncDisposable to automatically DELETE the async query.
/// </summary>
public sealed class EsqlAsyncQuery<T>(EsqlExecutor executor, EsqlResponse response) : IAsyncDisposable
{
	private bool _disposed;

	/// <summary>The async query ID (null if completed synchronously).</summary>
	public string? QueryId { get; } = response.Id;

	/// <summary>Whether the query is still running.</summary>
	public bool IsRunning => response.IsRunning;

	/// <summary>Whether results are immediately available.</summary>
	public bool IsCompleted => !IsRunning;

	/// <summary>Gets the initial response (may be partial if still running).</summary>
	public EsqlResponse Response => response;

	/// <summary>Waits for completion and returns materialized results.</summary>
	public async Task<List<T>> ToListAsync(CancellationToken ct = default)
	{
		var finalResponse = IsRunning && QueryId != null
			? await WaitForCompletionAsync(ct)
			: response;

		var materializer = new ResultMaterializer();
		var query = new EsqlQuery { ElementType = typeof(T) };
		return materializer.Materialize<T>(finalResponse, query).ToList();
	}

	/// <summary>Polls until query completes.</summary>
	public async Task<EsqlResponse> WaitForCompletionAsync(CancellationToken ct = default)
	{
		if (QueryId == null || !IsRunning)
			return response;

		EsqlResponse result;
		do
		{
			await Task.Delay(100, ct);
			result = await executor.GetAsyncStatusAsync(QueryId, ct);
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
			await executor.DeleteAsyncQueryAsync(QueryId);
		}
		catch
		{
			// Best effort cleanup - don't throw on dispose
		}
	}
}
