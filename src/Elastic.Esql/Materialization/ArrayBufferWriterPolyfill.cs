// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NETSTANDARD2_0

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>Minimal polyfill for <c>ArrayBufferWriter&lt;T&gt;</c> which is unavailable on netstandard2.0.</summary>
internal sealed class ArrayBufferWriter<T>(int initialCapacity) : IBufferWriter<T>
{
	private T[] _buffer = new T[initialCapacity];

	public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

	public int WrittenCount { get; private set; }

	public void ResetWrittenCount() => WrittenCount = 0;

	public void Advance(int count) => WrittenCount += count;

	public Memory<T> GetMemory(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsMemory(WrittenCount);
	}

	public Span<T> GetSpan(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsSpan(WrittenCount);
	}

	private void EnsureCapacity(int sizeHint)
	{
		if (sizeHint <= 0)
			sizeHint = 1;

		if (WrittenCount + sizeHint <= _buffer.Length)
			return;

		var newSize = Math.Max(_buffer.Length * 2, WrittenCount + sizeHint);
		var newBuffer = new T[newSize];
		Array.Copy(_buffer, newBuffer, WrittenCount);
		_buffer = newBuffer;
	}
}

#endif
