// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Lightweight synchronous buffer manager that wraps a <see cref="Stream"/> and provides
/// a read-advance pattern analogous to <c>PipeReader</c> but fully synchronous.
/// </summary>
internal sealed class SyncStreamBuffer(Stream stream, int initialBufferSize = 16384) : IDisposable
{
	private const int MinimumReadSize = 16384;

	private byte[] _buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
	private int _offset;
	private int _filled;
	private int _examined;
	// IDE0032 suggests an auto-property, but the field feeds two computed properties
	// (IsCompleted combines it with the offsets) and is written by the fill loop.
#pragma warning disable IDE0032
	private bool _streamCompleted;
#pragma warning restore IDE0032

	/// <summary>Whether the underlying stream has been fully consumed.</summary>
	public bool IsCompleted => _streamCompleted && _offset >= _filled;

	/// <summary>Whether the underlying stream has reached end of data. Unconsumed data may remain buffered.</summary>
	public bool IsEofReached => _streamCompleted;

	/// <summary>Returns the unconsumed data currently in the buffer as a <see cref="ReadOnlySequence{T}"/>.</summary>
	public ReadOnlySequence<byte> Buffer => new(_buffer, _offset, _filled - _offset);

	/// <summary>
	/// Reads more data from the underlying stream into the buffer.
	/// Returns <see langword="false"/> when the stream is exhausted and no unconsumed data remains.
	/// </summary>
	public bool Read()
	{
		// Unexamined buffered data satisfies the caller without touching the stream, mirroring
		// PipeReader semantics, so rows that already arrived are not held back by a blocking read.
		if (_examined < _filled)
			return true;

		if (_streamCompleted)
			return _offset < _filled;

		EnsureWritableSpace(MinimumReadSize);

		// Compaction in EnsureWritableSpace shifts absolute indexes; everything buffered has been
		// examined when this point is reached, so realign before appending new data.
		_examined = _filled;

		var bytesRead = stream.Read(_buffer, _filled, _buffer.Length - _filled);
		if (bytesRead == 0)
		{
			_streamCompleted = true;
			return _offset < _filled;
		}

		_filled += bytesRead;
		return true;
	}

	/// <summary>
	/// Advances past consumed data and records how far the caller examined, following the
	/// PipeReader calling pattern; the buffer retains all unconsumed data regardless.
	/// </summary>
	public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
	{
		_offset = consumed.GetInteger();
		_examined = examined.GetInteger();
	}

	/// <summary>Advances past consumed data (examined = end of buffer).</summary>
	public void AdvanceTo(SequencePosition consumed)
	{
		_offset = consumed.GetInteger();
		_examined = _filled;
	}

	public void Dispose()
	{
		var buf = _buffer;
		_buffer = null!;
		if (buf is not null)
			ArrayPool<byte>.Shared.Return(buf);
	}

	private void EnsureWritableSpace(int sizeHint)
	{
		var availableTail = _buffer.Length - _filled;
		if (availableTail >= sizeHint)
			return;

		var remaining = _filled - _offset;
		if (_offset > 0 && _buffer.Length - remaining >= sizeHint)
		{
			if (remaining > 0)
				System.Buffer.BlockCopy(_buffer, _offset, _buffer, 0, remaining);

			_filled = remaining;
			_offset = 0;
			return;
		}

		var required = remaining + sizeHint;
		var newSize = Math.Max(_buffer.Length * 2, required);
		var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

		if (remaining > 0)
			System.Buffer.BlockCopy(_buffer, _offset, newBuffer, 0, remaining);

		ArrayPool<byte>.Shared.Return(_buffer);
		_buffer = newBuffer;
		_filled = remaining;
		_offset = 0;
	}
}
