// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.Validation;
#if NET10_0_OR_GREATER
using System.Buffers;
using System.IO.Pipelines;
#endif

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Executes the query synchronously and returns the raw response body in the requested
	/// <see cref="EsqlFormat"/>. When <paramref name="format"/> is <c>null</c>, the query model's format applies,
	/// defaulting to JSON. The returned <see cref="Stream"/> owns the underlying HTTP
	/// response - dispose it to release the connection.
	/// </summary>
	public static Stream ToStream<TSource>(this IEsqlQueryable<TSource> source, EsqlFormat? format = null)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		var response = provider.Execute(source.Expression, format);
		return new OwnedSyncResponseStream(response);
	}

	/// <summary>
	/// Executes the query asynchronously and returns the raw response body in the requested
	/// <see cref="EsqlFormat"/>. When <paramref name="format"/> is <c>null</c>, the query model's format applies,
	/// defaulting to JSON. The returned <see cref="Stream"/> owns the underlying HTTP
	/// response - dispose it to release the connection.
	/// </summary>
	public static async Task<Stream> ToStreamAsync<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlFormat? format = null,
		CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		var response = await provider
			.ExecuteAsync(source.Expression, format, cancellationToken)
			.ConfigureAwait(false);

		return new OwnedAsyncResponseStream(response);
	}

#if NET10_0_OR_GREATER
	/// <summary>
	/// Executes the query asynchronously and returns the raw response body in the requested
	/// <see cref="EsqlFormat"/> as a <see cref="PipeReader"/>. When <paramref name="format"/> is <c>null</c>, the
	/// query model's format applies, defaulting to JSON. The reader owns the underlying
	/// HTTP response - complete or dispose it to release the connection.
	/// </summary>
	public static async Task<PipeReader> ToPipeReaderAsync<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlFormat? format = null,
		CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		var response = await provider
			.ExecuteAsync(source.Expression, format, cancellationToken)
			.ConfigureAwait(false);

		return new OwnedAsyncResponsePipeReader(response);
	}
#endif

	/// <summary>Submits the query as a server-side async raw ES|QL query and returns an <see cref="EsqlAsyncQuery"/>.</summary>
	public static EsqlAsyncQuery ToAsyncQuery<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlFormat format,
		EsqlAsyncQueryOptions? options = null)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		return provider.SubmitAsyncQuery(source.Expression, format, options);
	}

	/// <summary>Submits the query as a server-side async raw ES|QL query asynchronously and returns an <see cref="EsqlAsyncQuery"/>.</summary>
	public static Task<EsqlAsyncQuery> ToAsyncQueryAsync<TSource>(
		this IEsqlQueryable<TSource> source,
		EsqlFormat format,
		EsqlAsyncQueryOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		Verify.NotNull(source);

		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException("This method is only valid for EsqlQueryable.");

		return provider.SubmitAsyncQueryAsync(source.Expression, format, options, cancellationToken);
	}
}

/// <summary>Stream that wraps the body of a synchronous <see cref="IEsqlResponse"/> and disposes the response on disposal.</summary>
internal sealed class OwnedSyncResponseStream(IEsqlResponse response) : Stream
{
	private readonly IEsqlResponse _response = response;

	public override bool CanRead => _response.Body.CanRead;
	public override bool CanSeek => _response.Body.CanSeek;
	public override bool CanWrite => false;
	public override long Length => _response.Body.Length;

	public override long Position
	{
		get => _response.Body.Position;
		set => _response.Body.Position = value;
	}

	public override int Read(byte[] buffer, int offset, int count) =>
		_response.Body.Read(buffer, offset, count);

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		_response.Body.ReadAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
	public override int Read(Span<byte> buffer) => _response.Body.Read(buffer);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		_response.Body.ReadAsync(buffer, cancellationToken);
#endif

	public override long Seek(long offset, SeekOrigin origin) =>
		_response.Body.Seek(offset, origin);

	public override void SetLength(long value) =>
		throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) =>
		throw new NotSupportedException();

	public override void Flush() => _response.Body.Flush();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			_response.Dispose();
		base.Dispose(disposing);
	}
}

/// <summary>Stream that wraps the body of an asynchronous <see cref="IEsqlAsyncResponse"/> and disposes the response on disposal.</summary>
internal sealed class OwnedAsyncResponseStream : Stream
{
	private readonly IEsqlAsyncResponse _response;
	private readonly Stream _body;

	public OwnedAsyncResponseStream(IEsqlAsyncResponse response)
	{
		_response = response;
#if NET10_0_OR_GREATER
		_body = response.Body.AsStream();
#else
		_body = response.Body;
#endif
	}

	public override bool CanRead => _body.CanRead;
	public override bool CanSeek => _body.CanSeek;
	public override bool CanWrite => false;
	public override long Length => _body.Length;

	public override long Position
	{
		get => _body.Position;
		set => _body.Position = value;
	}

	public override int Read(byte[] buffer, int offset, int count) =>
		_body.Read(buffer, offset, count);

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		_body.ReadAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
	public override int Read(Span<byte> buffer) => _body.Read(buffer);

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		_body.ReadAsync(buffer, cancellationToken);
#endif

	public override long Seek(long offset, SeekOrigin origin) =>
		_body.Seek(offset, origin);

	public override void SetLength(long value) =>
		throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) =>
		throw new NotSupportedException();

	public override void Flush() => _body.Flush();

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			// Task.Run keeps the async disposal off the caller's SynchronizationContext so this blocking wait cannot deadlock.
			Task.Run(() => _response.DisposeAsync().AsTask()).GetAwaiter().GetResult();
		}

		base.Dispose(disposing);
	}

#if !NETSTANDARD2_0
	public override async ValueTask DisposeAsync()
	{
		await _response.DisposeAsync().ConfigureAwait(false);
		await base.DisposeAsync().ConfigureAwait(false);
	}
#endif
}

#if NET10_0_OR_GREATER
/// <summary>PipeReader that wraps the body of an asynchronous <see cref="IEsqlAsyncResponse"/> and disposes the response when completed.</summary>
internal sealed class OwnedAsyncResponsePipeReader(IEsqlAsyncResponse response) : PipeReader
{
	private readonly IEsqlAsyncResponse _response = response;
	private readonly PipeReader _inner = response.Body;
	private int _disposed;

	public override void AdvanceTo(SequencePosition consumed) =>
		_inner.AdvanceTo(consumed);

	public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
		_inner.AdvanceTo(consumed, examined);

	public override void CancelPendingRead() =>
		_inner.CancelPendingRead();

	public override void Complete(Exception? exception = null)
	{
		_inner.Complete(exception);
		DisposeResponse();
	}

	public override async ValueTask CompleteAsync(Exception? exception = null)
	{
		await _inner.CompleteAsync(exception).ConfigureAwait(false);

		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		await _response.DisposeAsync().ConfigureAwait(false);
	}

	public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
		_inner.ReadAsync(cancellationToken);

	public override bool TryRead(out ReadResult result) =>
		_inner.TryRead(out result);

	private void DisposeResponse()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;

		// Task.Run keeps the async disposal off the caller's SynchronizationContext so this blocking wait cannot deadlock.
		Task.Run(() => _response.DisposeAsync().AsTask()).GetAwaiter().GetResult();
	}
}
#endif
