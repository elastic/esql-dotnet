// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>Sync result wrapper. Owns stream buffer and optional read-ahead buffer.</summary>
internal sealed class EsqlResults<T> : IDisposable
{
	public IEnumerable<T> Rows { get; internal set; } = [];
	public string? Id { get; internal set; }
	public bool? IsRunning { get; internal set; }

	private IDisposable? _ownedResource;
	private byte[]? _rentedBuffer;

	internal void SetOwnedResource(IDisposable resource) => _ownedResource = resource;

	internal void SetBuffer(byte[] buffer) => _rentedBuffer = buffer;

	internal void ReleaseBuffer()
	{
		if (_rentedBuffer is null)
			return;

		ArrayPool<byte>.Shared.Return(_rentedBuffer);
		_rentedBuffer = null;
	}

	public void Dispose()
	{
		ReleaseBuffer();
		_ownedResource?.Dispose();
		_ownedResource = null;
	}
}

/// <summary>Async result wrapper. Owns stream buffer and optional read-ahead buffer.</summary>
internal sealed class EsqlAsyncResults<T> : IAsyncDisposable
{
	public IAsyncEnumerable<T> Rows { get; internal set; } = EmptyAsyncEnumerable<T>.Instance;
	public string? Id { get; internal set; }
	public bool? IsRunning { get; internal set; }

	private IDisposable? _ownedResource;
	private byte[]? _rentedBuffer;

	internal void SetOwnedResource(IDisposable resource) => _ownedResource = resource;

	internal void SetBuffer(byte[] buffer) => _rentedBuffer = buffer;

	internal void ReleaseBuffer()
	{
		if (_rentedBuffer is null)
			return;

		ArrayPool<byte>.Shared.Return(_rentedBuffer);
		_rentedBuffer = null;
	}

	public ValueTask DisposeAsync()
	{
		ReleaseBuffer();
		_ownedResource?.Dispose();
		_ownedResource = null;
		return default;
	}
}

internal sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
	public static readonly EmptyAsyncEnumerable<T> Instance = new();

	public T Current => default!;

	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

	public ValueTask<bool> MoveNextAsync() => new(false);

	public ValueTask DisposeAsync() => default;
}
