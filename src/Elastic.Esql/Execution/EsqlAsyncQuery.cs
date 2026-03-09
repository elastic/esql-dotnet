// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Execution;

/// <summary>
/// Represents an async ES|QL query that owns the response and auto-cleans up on disposal.
/// <para>
/// This type is <b>not thread-safe</b>. Do not call <see cref="RefreshAsync"/>/<see cref="Refresh"/>,
/// <see cref="WaitForCompletionAsync"/>/<see cref="WaitForCompletion"/>, or row enumeration concurrently.
/// </para>
/// </summary>
public sealed class EsqlAsyncQuery<T> : IAsyncDisposable, IDisposable
{
	private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
	private readonly IEsqlQueryExecutor _executor;
	private readonly EsqlResponseReader _reader;
	private readonly TimeSpan _pollInterval;
	private EsqlAsyncResponse<T>? _asyncResult;
	private EsqlResponse<T>? _syncResult;
	private IAsyncDisposable? _ownedAsyncResponse;
	private IDisposable? _ownedSyncResponse;
	private int _disposed;

	internal EsqlAsyncQuery(
		IEsqlQueryExecutor executor,
		EsqlAsyncResponse<T> result,
		IEsqlAsyncResponse response,
		EsqlResponseReader reader,
		TimeSpan pollInterval)
	{
		_executor = executor;
		_asyncResult = result;
		_ownedAsyncResponse = response;
		_reader = reader;
		_pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : DefaultPollInterval;

		QueryId = result.Metadata.Id;
		IsCompleted = !result.Metadata.IsRunning;
	}

	internal EsqlAsyncQuery(
		IEsqlQueryExecutor executor,
		EsqlResponse<T> result,
		IEsqlResponse response,
		EsqlResponseReader reader,
		TimeSpan pollInterval)
	{
		_executor = executor;
		_syncResult = result;
		_ownedSyncResponse = response;
		_reader = reader;
		_pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : DefaultPollInterval;

		QueryId = result.Metadata.Id;
		IsCompleted = !result.Metadata.IsRunning;
	}

	/// <summary>The async query ID (null if completed synchronously without <c>keep_on_completion</c>).</summary>
	public string? QueryId { get; private set; }

	/// <summary>Whether the query is still running. May reflect best-effort metadata after <see cref="RefreshAsync"/> or <see cref="Refresh"/>.</summary>
	public bool IsRunning => !IsCompleted;

	/// <summary>Whether the query has completed.</summary>
	public bool IsCompleted { get; private set; }

	/// <summary>
	/// Returns the rows from the current response as a lazy <see cref="IAsyncEnumerable{T}"/>.
	/// Each response's rows can only be consumed once (the underlying stream is single-read).
	/// After calling <see cref="RefreshAsync"/> or <see cref="WaitForCompletionAsync"/>,
	/// this returns rows from the new response.
	/// </summary>
	public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
		_asyncResult is not null
			? WithCancellation(_asyncResult.Rows, cancellationToken)
			: EmptyAsync();

	/// <summary>
	/// Performs a single poll to refresh the query state. Does not loop.
	/// <para>
	/// Metadata update is <b>best-effort</b>: <see cref="IsRunning"/> and <see cref="IsCompleted"/> are updated
	/// based on <c>id</c>/<c>is_running</c> properties found during the initial JSON parsing phase (before/around
	/// the <c>columns</c> array). If these properties appear after <c>values</c> in the response JSON, they will
	/// only be captured after <see cref="AsAsyncEnumerable"/> is fully consumed.
	/// Use <see cref="WaitForCompletionAsync"/> for guaranteed completion detection.
	/// </para>
	/// </summary>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		if (IsCompleted || QueryId is null)
			return;

		var response = await _executor.PollAsyncQueryAsync(QueryId, cancellationToken).ConfigureAwait(false);

		await DisposeOwnedResponseAsync().ConfigureAwait(false);
		_syncResult = null;
		_ownedAsyncResponse = response;

		_asyncResult = await _reader
			.ReadRowsWithMetadataAsync<T>(response.Body, cancellationToken)
			.ConfigureAwait(false);

		ApplyMetadata(_asyncResult.Metadata);
	}

	/// <summary>
	/// Polls until the query completes, using a lightweight metadata-only reader that guarantees
	/// <c>is_running</c> is resolved regardless of JSON property order. After completion, performs
	/// a final <see cref="RefreshAsync"/> to make the complete result available via <see cref="AsAsyncEnumerable"/>.
	/// </summary>
	public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
	{
		if (IsCompleted || QueryId is null)
			return;

		await PollUntilCompletedAsync(cancellationToken).ConfigureAwait(false);

		IsCompleted = true;
		await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Waits for completion if needed, then buffers all rows into a <see cref="List{T}"/>.</summary>
	public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
	{
		if (!IsCompleted)
			await WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

		var list = new List<T>();
		if (_asyncResult is not null)
		{
			await foreach (var item in _asyncResult.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
				list.Add(item);
		}

		return list;
	}

	/// <summary>Disposes the owned response and DELETEs the async query from the cluster (best-effort).</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		await DisposeOwnedResponseAsync().ConfigureAwait(false);

		if (QueryId is null)
			return;

		try
		{
			await _executor.DeleteAsyncQueryAsync(QueryId, default).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	/// <summary>
	/// Returns the rows from the current response as a lazy <see cref="IEnumerable{T}"/>.
	/// Each response's rows can only be consumed once (the underlying stream is single-read).
	/// After calling <see cref="Refresh"/> or <see cref="WaitForCompletion"/>,
	/// this returns rows from the new response.
	/// </summary>
	public IEnumerable<T> AsEnumerable() =>
		_syncResult?.Rows ?? [];

	/// <summary>Performs a single synchronous poll to refresh the query state. Does not loop.</summary>
	public void Refresh()
	{
		if (IsCompleted || QueryId is null)
			return;

		var response = _executor.PollAsyncQuery(QueryId);

		DisposeOwnedResponse();
		_asyncResult = null;
		_ownedSyncResponse = response;

		_syncResult = _reader.ReadRowsWithMetadata<T>(response.Body);
		ApplyMetadata(_syncResult.Metadata);
	}

	/// <summary>
	/// Polls synchronously until the query completes. After completion, performs
	/// a final <see cref="Refresh"/> to make the complete result available via <see cref="AsEnumerable"/>.
	/// </summary>
	public void WaitForCompletion()
	{
		if (IsCompleted || QueryId is null)
			return;

		PollUntilCompleted();

		IsCompleted = true;
		RefreshCore();
	}

	/// <summary>Waits for completion synchronously if needed, then buffers all rows into a <see cref="List{T}"/>.</summary>
	public List<T> ToList()
	{
		if (!IsCompleted)
			WaitForCompletion();

		return _syncResult is not null
			? [.. _syncResult.Rows]
			: [];
	}

	/// <summary>Disposes the owned response and DELETEs the async query from the cluster (best-effort).</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		DisposeOwnedResponse();

		if (QueryId is null)
			return;

		try
		{
			_executor.DeleteAsyncQuery(QueryId);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	private void ApplyMetadata(EsqlStreamMetadata metadata)
	{
		if (metadata.Id is not null)
			QueryId = metadata.Id;

		if (!metadata.IsRunning)
			IsCompleted = true;
	}

	private async Task PollUntilCompletedAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			var response = await _executor.PollAsyncQueryAsync(QueryId!, cancellationToken).ConfigureAwait(false);

			EsqlStreamMetadata metadata;
			try
			{
				metadata = await EsqlResponseReader
					.ReadMetadataAsync(response.Body, cancellationToken)
					.ConfigureAwait(false);
			}
			finally
			{
				await response.DisposeAsync().ConfigureAwait(false);
			}

			if (metadata.Id is not null)
				QueryId = metadata.Id;

			if (!metadata.IsRunning)
				break;

			await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
		}
	}

	private void PollUntilCompleted()
	{
		while (true)
		{
			using var response = _executor.PollAsyncQuery(QueryId!);
			var metadata = EsqlResponseReader.ReadMetadata(response.Body);

			if (metadata.Id is not null)
				QueryId = metadata.Id;

			if (!metadata.IsRunning)
				break;

			Thread.Sleep(_pollInterval);
		}
	}

	private async Task RefreshCoreAsync(CancellationToken cancellationToken)
	{
		if (QueryId is null)
			return;

		var response = await _executor.PollAsyncQueryAsync(QueryId, cancellationToken).ConfigureAwait(false);

		await DisposeOwnedResponseAsync().ConfigureAwait(false);
		_ownedAsyncResponse = response;

		_asyncResult = await _reader.ReadRowsWithMetadataAsync<T>(
			response.Body, cancellationToken).ConfigureAwait(false);
	}

	private void RefreshCore()
	{
		if (QueryId is null)
			return;

		var response = _executor.PollAsyncQuery(QueryId);

		DisposeOwnedResponse();
		_asyncResult = null;
		_ownedSyncResponse = response;

		_syncResult = _reader.ReadRowsWithMetadata<T>(response.Body);
	}

	private void DisposeOwnedResponse()
	{
		_ownedAsyncResponse?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_ownedAsyncResponse = null;
		_ownedSyncResponse?.Dispose();
		_ownedSyncResponse = null;
	}

	private async ValueTask DisposeOwnedResponseAsync()
	{
		if (_ownedAsyncResponse is not null)
		{
			await _ownedAsyncResponse.DisposeAsync().ConfigureAwait(false);
			_ownedAsyncResponse = null;
		}

		_ownedSyncResponse?.Dispose();
		_ownedSyncResponse = null;
	}

	private static async IAsyncEnumerable<T> WithCancellation(
		IAsyncEnumerable<T> source,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
			yield return item;
	}

#pragma warning disable CS1998 // Async method lacks 'await' operators
	private static async IAsyncEnumerable<T> EmptyAsync()
#pragma warning restore CS1998
	{
		yield break;
	}
}
