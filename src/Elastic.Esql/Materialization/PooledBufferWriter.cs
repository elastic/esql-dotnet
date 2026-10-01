// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Diagnostics;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Growable byte buffer backed by <see cref="ArrayPool{T}.Shared"/>. Growth swaps in a larger rented array and
/// returns the old one, so scratch buffers leave no garbage behind however large a row or batch gets.
/// </summary>
internal sealed class PooledBufferWriter(int initialCapacity) : IBufferWriter<byte>, IDisposable
{
	private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);

	public int WrittenCount { get; private set; }

	public ReadOnlySpan<byte> WrittenSpan => Buffer.AsSpan(0, WrittenCount);

	private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));

	public void ResetWrittenCount() => WrittenCount = 0;

	public void Advance(int count)
	{
		Debug.Assert(WrittenCount + count <= Buffer.Length, "Advance past the rented capacity.");
		WrittenCount += count;
	}

	public Memory<byte> GetMemory(int sizeHint = 0) => EnsureCapacity(sizeHint).AsMemory(WrittenCount);

	public Span<byte> GetSpan(int sizeHint = 0) => EnsureCapacity(sizeHint).AsSpan(WrittenCount);

	private byte[] EnsureCapacity(int sizeHint)
	{
		var buffer = Buffer;
		if (sizeHint <= 0)
			sizeHint = 1;

		if (WrittenCount + sizeHint <= buffer.Length)
			return buffer;

		var grown = ArrayPool<byte>.Shared.Rent(Math.Max(buffer.Length * 2, WrittenCount + sizeHint));
		buffer.AsSpan(0, WrittenCount).CopyTo(grown);
		ArrayPool<byte>.Shared.Return(buffer);
		_buffer = grown;
		return grown;
	}

	public void Dispose()
	{
		var buffer = _buffer;
		_buffer = null;
		if (buffer is not null)
			ArrayPool<byte>.Shared.Return(buffer);
	}
}
