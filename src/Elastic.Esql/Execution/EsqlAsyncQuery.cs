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
	private readonly object? _queryOptions;
	private EsqlAsyncResults<T>? _asyncResult;
	private EsqlResults<T>? _syncResult;
	private IAsyncDisposable? _ownedAsyncResponse;
	private IDisposable? _ownedSyncResponse;
	private int _disposed;

	/// <summary>Constructs from an async transport response.</summary>
	internal EsqlAsyncQuery(
		IEsqlQueryExecutor executor,
		EsqlAsyncResults<T> result,
		IEsqlAsyncResponse response,
		EsqlResponseReader reader,
		object? queryOptions)
	{
		_executor = executor;
		_asyncResult = result;
		_ownedAsyncResponse = response;
		_reader = reader;
		_queryOptions = queryOptions;

		QueryId = result.Id;
		IsCompleted = result.IsRunning != true;
	}

	/// <summary>Constructs from a sync transport response.</summary>
	internal EsqlAsyncQuery(
		IEsqlQueryExecutor executor,
		EsqlResults<T> result,
		IEsqlResponse response,
		EsqlResponseReader reader,
		object? queryOptions)
	{
		_executor = executor;
		_syncResult = result;
		_ownedSyncResponse = response;
		_reader = reader;
		_queryOptions = queryOptions;

		QueryId = result.Id;
		IsCompleted = result.IsRunning != true;
	}

	/// <summary>The async query ID (null if completed synchronously without <c>keep_on_completion</c>).</summary>
	public string? QueryId { get; private set; }

	/// <summary>Whether the query is still running. May reflect best-effort metadata after <see cref="RefreshAsync"/> or <see cref="Refresh"/>.</summary>
	public bool IsRunning => !IsCompleted;

	/// <summary>Whether the query has completed.</summary>
	public bool IsCompleted { get; private set; }

	/// <summary>
	/// Waits for the query to complete if still running, then returns the rows as a lazy <see cref="IAsyncEnumerable{T}"/>.
	/// Calls <see cref="WaitForCompletionAsync"/> internally before returning rows.
	/// Each response's rows can only be consumed once (the underlying stream is single-read).
	/// </summary>
	public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		if (!IsCompleted)
			await WaitForCompletionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

		var source = _asyncResult?.Rows
			?? (_syncResult is not null ? new SyncToAsyncEnumerable(_syncResult.Rows) : null);

		if (source is null)
			yield break;

		await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
			yield return item;
	}

	/// <summary>
	/// Waits for the query to complete if still running, then returns the rows as a lazy <see cref="IEnumerable{T}"/>.
	/// Calls <see cref="WaitForCompletion"/> internally before returning rows.
	/// Each response's rows can only be consumed once (the underlying stream is single-read).
	/// </summary>
	public IEnumerable<T> AsEnumerable()
	{
		if (!IsCompleted)
			WaitForCompletion();

		if (_syncResult is not null)
			return _syncResult.Rows;

		if (_asyncResult is not null)
			return new AsyncToSyncEnumerable(_asyncResult.Rows);

		return [];
	}

	/// <summary>
	/// Performs a single poll to refresh the query state.
	/// </summary>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot refresh an async query without a query ID.");

		var response = await _executor.PollAsyncQueryAsync(QueryId, _queryOptions, cancellationToken).ConfigureAwait(false);

		await DisposeOwnedResponseAsync().ConfigureAwait(false);
		DisposeResults();
		_ownedAsyncResponse = response;

		_asyncResult = await _reader.ReadRowsAsync<T>(response.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
		_syncResult = null;
		ApplyMetadata(_asyncResult);
	}

	/// <summary>
	/// Polls until the query completes.
	/// </summary>
	public async Task WaitForCompletionAsync(TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot wait for completion of an async query without a query ID.");

		var interval = ResolvePollInterval(pollInterval);

		while (true)
		{
			var response = await _executor.PollAsyncQueryAsync(QueryId, _queryOptions, cancellationToken).ConfigureAwait(false);

			await DisposeOwnedResponseAsync().ConfigureAwait(false);
			DisposeResults();
			_ownedAsyncResponse = response;

			_asyncResult = await _reader.ReadRowsAsync<T>(response.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
			_syncResult = null;
			ApplyMetadata(_asyncResult);

			if (IsCompleted)
				return;

			await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Disposes the owned response and DELETEs the async query from the cluster (best-effort).</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		DisposeResults();
		await DisposeOwnedResponseAsync().ConfigureAwait(false);

		if (QueryId is null)
			return;

		try
		{
			await _executor.DeleteAsyncQueryAsync(QueryId, _queryOptions, default).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	/// <summary>Performs a single synchronous poll to refresh the query state. Does not loop.</summary>
	public void Refresh()
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot refresh an async query without a query ID.");

		var response = _executor.PollAsyncQuery(QueryId, _queryOptions);

		DisposeOwnedResponse();
		DisposeResults();
		_ownedSyncResponse = response;

		_syncResult = _reader.ReadRows<T>(response.Body);
		_asyncResult = null;
		ApplyMetadata(_syncResult);
	}

	/// <summary>
	/// Polls synchronously until the query completes. When <c>is_running: true</c>, the response reader
	/// returns immediately with empty rows. The final poll's result contains the rows.
	/// </summary>
	public void WaitForCompletion(TimeSpan? pollInterval = null)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot wait for completion of an async query without a query ID.");

		var interval = ResolvePollInterval(pollInterval);

		while (true)
		{
			var response = _executor.PollAsyncQuery(QueryId, _queryOptions);

			DisposeOwnedResponse();
			DisposeResults();
			_ownedSyncResponse = response;

			_syncResult = _reader.ReadRows<T>(response.Body);
			_asyncResult = null;
			ApplyMetadata(_syncResult);

			if (IsCompleted)
				return;

			Thread.Sleep(interval);
		}
	}

	/// <summary>Waits for completion synchronously if needed, then buffers all rows into a <see cref="List{T}"/>.</summary>
	public List<T> ToList(TimeSpan? pollInterval = null)
	{
		if (!IsCompleted)
			WaitForCompletion(pollInterval);

		return [.. AsEnumerable()];
	}

	/// <summary>Disposes the owned response and DELETEs the async query from the cluster (best-effort).</summary>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		DisposeResults();
		DisposeOwnedResponse();

		if (QueryId is null)
			return;

		try
		{
			_executor.DeleteAsyncQuery(QueryId, _queryOptions);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	private static TimeSpan ResolvePollInterval(TimeSpan? pollInterval)
	{
		if (pollInterval is null)
			return DefaultPollInterval;

		if (pollInterval.Value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(pollInterval), "The poll interval must be greater than zero.");

		return pollInterval.Value;
	}

	private void ApplyMetadata(EsqlAsyncResults<T> result)
	{
		if (result.Id is not null)
			QueryId = result.Id;

		if (result.IsRunning == false)
			IsCompleted = true;
	}

	private void ApplyMetadata(EsqlResults<T> result)
	{
		if (result.Id is not null)
			QueryId = result.Id;

		if (result.IsRunning == false)
			IsCompleted = true;
	}

	private void DisposeResults()
	{
		_asyncResult?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_asyncResult = null;
		_syncResult?.Dispose();
		_syncResult = null;
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

	private readonly struct CancellableAsyncEnumerable(
		IAsyncEnumerable<T> source,
		CancellationToken cancellationToken) : IAsyncEnumerable<T>
	{
		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken enumeratorCancellationToken = default)
		{
			var effectiveCancellationToken = enumeratorCancellationToken.CanBeCanceled
				? enumeratorCancellationToken
				: cancellationToken;

			return source.GetAsyncEnumerator(effectiveCancellationToken);
		}
	}

	private sealed class SyncToAsyncEnumerable(IEnumerable<T> source) : IAsyncEnumerable<T>
	{
		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
			new Enumerator(source.GetEnumerator(), cancellationToken);

		private sealed class Enumerator(IEnumerator<T> inner, CancellationToken ct) : IAsyncEnumerator<T>
		{
			public T Current => inner.Current;

			public ValueTask<bool> MoveNextAsync()
			{
				ct.ThrowIfCancellationRequested();
				return new ValueTask<bool>(inner.MoveNext());
			}

			public ValueTask DisposeAsync()
			{
				inner.Dispose();
				return default;
			}
		}
	}

	private sealed class AsyncToSyncEnumerable(IAsyncEnumerable<T> source) : IEnumerable<T>
	{
		public IEnumerator<T> GetEnumerator() =>
			new Enumerator(source.GetAsyncEnumerator());

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		private sealed class Enumerator(IAsyncEnumerator<T> inner) : IEnumerator<T>
		{
			public T Current => inner.Current;

			object? System.Collections.IEnumerator.Current => Current;

			public bool MoveNext() =>
				inner.MoveNextAsync().AsTask().GetAwaiter().GetResult();

			public void Reset() =>
				throw new NotSupportedException();

			public void Dispose() =>
				inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
		}
	}
}
