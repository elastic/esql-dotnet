// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Elastic.Esql.Materialization;

/// <summary>
/// Lightweight buffer manager that wraps a <see cref="Stream"/> and provides a read-advance pattern analogous to
/// <c>PipeReader</c> while remaining stream based, refilled by a blocking or an awaited read.
/// </summary>
/// <remarks>
/// The fill loop needs MinimumReadSize free bytes, and a 16 KB rent regrows to 32 KB on the second read
/// whenever a partial row remains, so the default starts there.
/// </remarks>
internal sealed class StreamBuffer(Stream stream, int initialBufferSize = 32768) : IAsyncBufferCursor, ISyncBufferCursor, IDisposable
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
#if !NETSTANDARD2_0
	[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
	public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
	{
		if (TryAnswerFromBuffer(out var hasData))
			return hasData;

#if NETSTANDARD2_0
		var bytesRead = await stream
			.ReadAsync(_buffer, _filled, _buffer.Length - _filled, cancellationToken)
			.ConfigureAwait(false);
#else
		var bytesRead = await stream
			.ReadAsync(_buffer.AsMemory(_filled, _buffer.Length - _filled), cancellationToken)
			.ConfigureAwait(false);
#endif

		return CompleteRead(bytesRead);
	}

	/// <inheritdoc cref="ReadAsync"/>
	public bool Read() =>
		TryAnswerFromBuffer(out var hasData) ? hasData : CompleteRead(stream.Read(_buffer, _filled, _buffer.Length - _filled));

	/// <summary>
	/// Advances past consumed data and records how far the caller examined, following the
	/// PipeReader calling pattern; the buffer retains all unconsumed data regardless.
	/// </summary>
	public void AdvanceTo(SequencePosition consumed, SequencePosition examined)
	{
		_offset = consumed.GetInteger();
		_examined = examined.GetInteger();
	}

	public void Dispose()
	{
		var buf = _buffer;
		_buffer = null!;
		if (buf is not null)
			ArrayPool<byte>.Shared.Return(buf);
	}

	/// <summary>
	/// Answers a read from buffered state when it can. Otherwise makes room for the stream read that follows and
	/// returns <see langword="false"/>.
	/// </summary>
	private bool TryAnswerFromBuffer(out bool hasData)
	{
		// Unexamined buffered data satisfies the caller without touching the stream, mirroring
		// PipeReader.ReadAsync, so rows that already arrived are not held back by a network read.
		if (_examined < _filled)
		{
			hasData = true;
			return true;
		}

		if (_streamCompleted)
		{
			hasData = _offset < _filled;
			return true;
		}

		EnsureWritableSpace(MinimumReadSize);

		// Compaction in EnsureWritableSpace shifts absolute indexes. Everything buffered has been
		// examined when this point is reached, so realign before appending new data.
		_examined = _filled;
		hasData = false;
		return false;
	}

	private bool CompleteRead(int bytesRead)
	{
		if (bytesRead == 0)
		{
			_streamCompleted = true;
			return _offset < _filled;
		}

		_filled += bytesRead;
		return true;
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

		PooledArrays.Grow(ref _buffer, _offset, remaining, Math.Max(_buffer.Length * 2, remaining + sizeHint));
		_filled = remaining;
		_offset = 0;
	}
}
