// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Lightweight synchronous buffer manager that wraps a <see cref="Stream"/> and provides
/// a read-advance pattern analogous to <c>PipeReader</c> but fully synchronous.
/// </summary>
internal sealed class SyncStreamBuffer(Stream stream, int initialBufferSize = 4096) : IDisposable
{
	private byte[] _buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
	private int _offset;
	private int _filled;
	private bool _streamCompleted;

	/// <summary>Whether the underlying stream has been fully consumed.</summary>
	public bool IsCompleted => _streamCompleted && _offset >= _filled;

	/// <summary>Returns the unconsumed data currently in the buffer as a <see cref="ReadOnlySequence{T}"/>.</summary>
	public ReadOnlySequence<byte> Buffer => new(_buffer, _offset, _filled - _offset);

	/// <summary>
	/// Reads more data from the underlying stream into the buffer.
	/// Returns <see langword="false"/> when the stream is exhausted and no unconsumed data remains.
	/// </summary>
	public bool Read()
	{
		if (_streamCompleted)
			return _offset < _filled;

		Compact();
		EnsureCapacity();

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
	/// Advances past consumed data. The <paramref name="examined"/> position is accepted for API
	/// compatibility with the PipeReader calling pattern but is not used — the sync buffer retains
	/// all unconsumed data regardless.
	/// </summary>
	public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
	{
		_ = examined;
		_offset = consumed.GetInteger();
	}

	/// <summary>Advances past consumed data (examined = end of buffer).</summary>
	public void AdvanceTo(SequencePosition consumed) => _offset = consumed.GetInteger();

	public void Dispose()
	{
		var buf = _buffer;
		_buffer = null!;
		if (buf is not null)
			ArrayPool<byte>.Shared.Return(buf);
	}

	private void Compact()
	{
		if (_offset == 0)
			return;

		var remaining = _filled - _offset;
		if (remaining > 0)
			System.Buffer.BlockCopy(_buffer, _offset, _buffer, 0, remaining);

		_filled = remaining;
		_offset = 0;
	}

	private void EnsureCapacity()
	{
		if (_filled < _buffer.Length)
			return;

		var newSize = _buffer.Length * 2;
		var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
		System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _filled);
		ArrayPool<byte>.Shared.Return(_buffer);
		_buffer = newBuffer;
	}
}
