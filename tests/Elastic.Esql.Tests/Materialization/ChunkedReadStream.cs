// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Materialization;

/// <summary>
/// Wraps a byte payload and returns at most <c>maxBytesPerRead</c> bytes per read so tests can
/// exercise parser resumption across chunk boundaries.
/// </summary>
internal sealed class ChunkedReadStream(byte[] data, int maxBytesPerRead) : Stream
{
	private int _position;

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => data.Length;

	public override long Position
	{
		get => _position;
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		var remaining = data.Length - _position;
		if (remaining == 0)
			return 0;

		var toCopy = Math.Min(Math.Min(count, maxBytesPerRead), remaining);
		Array.Copy(data, _position, buffer, offset, toCopy);
		_position += toCopy;
		return toCopy;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		Task.FromResult(Read(buffer, offset, count));

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		var remaining = data.Length - _position;
		if (remaining == 0)
			return ValueTask.FromResult(0);

		var toCopy = Math.Min(Math.Min(buffer.Length, maxBytesPerRead), remaining);
		data.AsSpan(_position, toCopy).CopyTo(buffer.Span);
		_position += toCopy;
		return ValueTask.FromResult(toCopy);
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
