// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;
using Elastic.Esql.Materialization;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif

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
	private readonly EsqlExecutionRequest _request;
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
		EsqlExecutionRequest request)
	{
		_executor = executor;
		_asyncResult = result;
		_ownedAsyncResponse = response;
		_reader = reader;
		_request = request;

		QueryId = result.Id ?? ReadAsyncIdHeader(response);
		IsCompleted = result.IsRunning != true;
	}

	/// <summary>Constructs from a sync transport response.</summary>
	internal EsqlAsyncQuery(
		IEsqlQueryExecutor executor,
		EsqlResults<T> result,
		IEsqlResponse response,
		EsqlResponseReader reader,
		EsqlExecutionRequest request)
	{
		_executor = executor;
		_syncResult = result;
		_ownedSyncResponse = response;
		_reader = reader;
		_request = request;

		QueryId = result.Id ?? ReadAsyncIdHeader(response);
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

		SyncQueryIdFromResults();
	}

	/// <summary>
	/// Waits for the query to complete if still running, then returns the rows as a lazy <see cref="IEnumerable{T}"/>.
	/// Calls <see cref="WaitForCompletion"/> internally before returning rows.
	/// Each response's rows can only be consumed once (the underlying stream is single-read).
	/// </summary>
	/// <remarks>
	/// When the query was submitted asynchronously, enumeration bridges async reads onto the calling thread
	/// via the thread pool. Prefer <see cref="AsAsyncEnumerable"/> with <c>await foreach</c>.
	/// </remarks>
	public IEnumerable<T> AsEnumerable()
	{
		if (!IsCompleted)
			WaitForCompletion();

		if (_syncResult is not null)
			return EnumerateThenSyncQueryId(_syncResult.Rows);

		if (_asyncResult is not null)
			return EnumerateThenSyncQueryId(new AsyncToSyncEnumerable(_asyncResult.Rows));

		return [];
	}

	private IEnumerable<T> EnumerateThenSyncQueryId(IEnumerable<T> rows)
	{
		foreach (var item in rows)
			yield return item;

		SyncQueryIdFromResults();
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

		var response = await _executor.PollAsyncQueryAsync(QueryId, _request, cancellationToken).ConfigureAwait(false);

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
			var response = await _executor.PollAsyncQueryAsync(QueryId, _request, cancellationToken).ConfigureAwait(false);

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
			await _executor.DeleteAsyncQueryAsync(QueryId, _request, default).ConfigureAwait(false);
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

		var response = _executor.PollAsyncQuery(QueryId, _request);

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
			var response = _executor.PollAsyncQuery(QueryId, _request);

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
	/// <remarks>May block while releasing an asynchronously-submitted response. Prefer <see cref="DisposeAsync"/>.</remarks>
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
			_executor.DeleteAsyncQuery(QueryId, _request);
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

	private static string? ReadAsyncIdHeader(IEsqlAsyncResponse response) =>
		response.TryGetHeader("X-Elasticsearch-Async-Id", out var values) ? values.FirstOrDefault() : null;

	private static string? ReadAsyncIdHeader(IEsqlResponse response) =>
		response.TryGetHeader("X-Elasticsearch-Async-Id", out var values) ? values.FirstOrDefault() : null;

	/// <summary>The reader captures a trailing "id" property only once row enumeration has completed.</summary>
	private void SyncQueryIdFromResults() =>
		QueryId ??= _asyncResult?.Id ?? _syncResult?.Id;

	private void DisposeResults()
	{
		_asyncResult?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_asyncResult = null;
		_syncResult?.Dispose();
		_syncResult = null;
	}

	private void DisposeOwnedResponse()
	{
		if (_ownedAsyncResponse is { } response)
		{
			_ownedAsyncResponse = null;
			// Task.Run keeps the async disposal off the caller's SynchronizationContext so this blocking wait cannot deadlock.
			Task.Run(() => response.DisposeAsync().AsTask()).GetAwaiter().GetResult();
		}

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

			public bool MoveNext()
			{
				// Without an ambient SynchronizationContext, blocking inline is deadlock-free and
				// avoids a per-row thread-pool hop; the Task.Run detour is only needed when a
				// context could capture the continuation.
				if (SynchronizationContext.Current is null)
					return inner.MoveNextAsync().AsTask().GetAwaiter().GetResult();

				return Task.Run(() => inner.MoveNextAsync().AsTask()).GetAwaiter().GetResult();
			}

			public void Reset() =>
				throw new NotSupportedException();

			public void Dispose() =>
				Task.Run(() => inner.DisposeAsync().AsTask()).GetAwaiter().GetResult();
		}
	}
}

/// <summary>
/// Non-generic counterpart to <see cref="EsqlAsyncQuery{T}"/> that returns the raw, server-formatted
/// response body (CSV, JSON, Arrow, etc.) instead of materialised POCO rows and auto-cleans up on disposal.
/// <para>
/// This type is <b>not thread-safe</b>. Do not call <see cref="RefreshAsync"/>/<see cref="Refresh"/>,
/// <see cref="WaitForCompletionAsync"/>/<see cref="WaitForCompletion"/>, or row enumeration concurrently.
/// </para>
/// </summary>
public sealed class EsqlAsyncQuery : IAsyncDisposable, IDisposable
{
	private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

	private readonly IEsqlQueryExecutor _executor;
	private readonly EsqlExecutionRequest _request;
	private IEsqlAsyncResponse? _ownedAsyncResponse;
	private IEsqlResponse? _ownedSyncResponse;
	private int _disposed;

	internal EsqlAsyncQuery(IEsqlQueryExecutor executor, IEsqlAsyncResponse response, EsqlExecutionRequest request)
	{
		_executor = executor;
		_ownedAsyncResponse = response;
		Format = request.Format ?? EsqlFormat.Json;
		_request = request;
		ApplyHeaderMetadata(response);
	}

	internal EsqlAsyncQuery(IEsqlQueryExecutor executor, IEsqlResponse response, EsqlExecutionRequest request)
	{
		_executor = executor;
		_ownedSyncResponse = response;
		Format = request.Format ?? EsqlFormat.Json;
		_request = request;
		ApplyHeaderMetadata(response);
	}

	/// <summary>The async query ID (null if completed synchronously without <c>keep_on_completion</c>).</summary>
	public string? QueryId { get; private set; }

	/// <summary>Whether the query is still running according to the most recent poll.</summary>
	public bool IsRunning { get; private set; }

	/// <summary>Whether the query has completed.</summary>
	public bool IsCompleted => !IsRunning;

	/// <summary>The wire-level response format requested at submission time.</summary>
	public EsqlFormat Format { get; }

	/// <summary>Returns the response body of the most recent (completed) poll as a <see cref="Stream"/>.</summary>
	/// <exception cref="InvalidOperationException">Thrown if the query is not yet completed.</exception>
	public Stream GetResponseStream()
	{
		ThrowIfNotCompleted();

		if (_ownedSyncResponse is { } sync)
			return sync.Body;

#if NET10_0_OR_GREATER
		if (_ownedAsyncResponse is { } asyncResp)
			return asyncResp.Body.AsStream();
#else
		if (_ownedAsyncResponse is { } asyncResp)
			return asyncResp.Body;
#endif

		throw new InvalidOperationException("No response body is available.");
	}

#if NET10_0_OR_GREATER
	/// <summary>Returns the response body of the most recent (completed) poll as a <see cref="PipeReader"/>.</summary>
	/// <exception cref="InvalidOperationException">Thrown if the query is not yet completed.</exception>
	public PipeReader GetResponsePipeReader()
	{
		ThrowIfNotCompleted();

		if (_ownedAsyncResponse is { } asyncResp)
			return asyncResp.Body;

		if (_ownedSyncResponse is { } sync)
			return PipeReader.Create(sync.Body);

		throw new InvalidOperationException("No response body is available.");
	}
#endif

	/// <summary>Performs a single poll to refresh the query state.</summary>
	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot refresh an async query without a query ID.");

		var response = await _executor
			.PollAsyncQueryAsync(QueryId, _request, cancellationToken)
			.ConfigureAwait(false);

		await DisposeOwnedResponseAsync().ConfigureAwait(false);
		_ownedAsyncResponse = response;
		ApplyHeaderMetadata(response);
	}

	/// <summary>Polls until the query completes.</summary>
	public async Task WaitForCompletionAsync(TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot wait for completion of an async query without a query ID.");

		var interval = ResolvePollInterval(pollInterval);

		while (!IsCompleted)
		{
			await RefreshAsync(cancellationToken).ConfigureAwait(false);

			if (IsCompleted)
				return;

			await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Performs a single synchronous poll to refresh the query state.</summary>
	public void Refresh()
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot refresh an async query without a query ID.");

		var response = _executor.PollAsyncQuery(QueryId, _request);

		DisposeOwnedResponse();
		_ownedSyncResponse = response;
		ApplyHeaderMetadata(response);
	}

	/// <summary>Polls synchronously until the query completes.</summary>
	public void WaitForCompletion(TimeSpan? pollInterval = null)
	{
		if (IsCompleted)
			return;

		if (QueryId is null)
			throw new InvalidOperationException("Cannot wait for completion of an async query without a query ID.");

		var interval = ResolvePollInterval(pollInterval);

		while (!IsCompleted)
		{
			Refresh();

			if (IsCompleted)
				return;

			Thread.Sleep(interval);
		}
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
			await _executor.DeleteAsyncQueryAsync(QueryId, _request, default).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	/// <summary>Disposes the owned response and DELETEs the async query from the cluster (best-effort).</summary>
	/// <remarks>May block while releasing an asynchronously-submitted response. Prefer <see cref="DisposeAsync"/>.</remarks>
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		DisposeOwnedResponse();

		if (QueryId is null)
			return;

		try
		{
			_executor.DeleteAsyncQuery(QueryId, _request);
		}
		catch (Exception)
		{
			// Best-effort cleanup; executor may throw transport-specific exceptions
		}
	}

	private void ApplyHeaderMetadata(IEsqlResponse response)
	{
		if (response.TryGetHeader("X-Elasticsearch-Async-Id", out var idValues))
			QueryId = idValues.FirstOrDefault() ?? QueryId;

		IsRunning = response.TryGetHeader("X-Elasticsearch-Async-Is-Running", out var runningValues)
			&& ParseIsRunning(runningValues);
	}

	private void ApplyHeaderMetadata(IEsqlAsyncResponse response)
	{
		if (response.TryGetHeader("X-Elasticsearch-Async-Id", out var idValues))
			QueryId = idValues.FirstOrDefault() ?? QueryId;

		IsRunning = response.TryGetHeader("X-Elasticsearch-Async-Is-Running", out var runningValues)
			&& ParseIsRunning(runningValues);
	}

	private static bool ParseIsRunning(IEnumerable<string> values)
	{
		var raw = values.FirstOrDefault();
		if (raw is null)
			return false;

		// Elasticsearch emits this header as an RFC 8941 structured-field boolean: "?1" = true, "?0" = false.
		// Some intermediaries (or earlier server versions) may send the textual "true"/"false" instead.
		return raw switch
		{
			"?1" => true,
			"?0" => false,
			_ => bool.TryParse(raw, out var parsed) && parsed
		};
	}

	private void ThrowIfNotCompleted()
	{
		if (!IsCompleted)
			throw new InvalidOperationException("The async query has not completed yet. Call WaitForCompletion(Async) or Refresh(Async) until IsCompleted is true.");
	}

	private static TimeSpan ResolvePollInterval(TimeSpan? pollInterval)
	{
		if (pollInterval is null)
			return DefaultPollInterval;

		if (pollInterval.Value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(pollInterval), "The poll interval must be greater than zero.");

		return pollInterval.Value;
	}

	private void DisposeOwnedResponse()
	{
		if (_ownedAsyncResponse is { } response)
		{
			_ownedAsyncResponse = null;
			// Task.Run keeps the async disposal off the caller's SynchronizationContext so this blocking wait cannot deadlock.
			Task.Run(() => response.DisposeAsync().AsTask()).GetAwaiter().GetResult();
		}

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
}
