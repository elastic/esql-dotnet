// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Growable byte buffer backed by <see cref="ArrayPool{T}.Shared"/>. Growth swaps in a larger rented array and
/// returns the old one, so scratch buffers leave no garbage behind however large a row or batch gets.
/// </summary>
internal sealed class PooledBufferWriter(int initialCapacity) : IDisposable
{
	private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);

	public int WrittenCount { get; private set; }

	public ReadOnlySpan<byte> WrittenSpan => Buffer.AsSpan(0, WrittenCount);

	private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledBufferWriter));

	public void ResetWrittenCount() => WrittenCount = 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write(byte value)
	{
		EnsureCapacity(1)[WrittenCount] = value;
		WrittenCount++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write(ReadOnlySpan<byte> value)
	{
		value.CopyTo(EnsureCapacity(value.Length).AsSpan(WrittenCount));
		WrittenCount += value.Length;
	}

	public void Write(in ReadOnlySequence<byte> value)
	{
		var length = checked((int)value.Length);
		value.CopyTo(EnsureCapacity(length).AsSpan(WrittenCount));
		WrittenCount += length;
	}

	private byte[] EnsureCapacity(int sizeHint)
	{
		var buffer = Buffer;
		if (WrittenCount + sizeHint <= buffer.Length)
			return buffer;

		PooledArrays.Grow(ref buffer, 0, WrittenCount, Math.Max(buffer.Length * 2, WrittenCount + sizeHint));
		_buffer = buffer;
		return buffer;
	}

	public void Dispose()
	{
		var buffer = _buffer;
		_buffer = null;
		if (buffer is not null)
			ArrayPool<byte>.Shared.Return(buffer);
	}
}
