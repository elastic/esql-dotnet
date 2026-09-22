// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>Read-and-advance view over buffered response bytes, mirroring PipeReader.</summary>
internal interface IBufferCursor
{
	ReadOnlySequence<byte> Buffer { get; }
	bool IsCompleted { get; }
	bool IsEofReached { get; }
	void AdvanceTo(SequencePosition consumed, SequencePosition examined);
}

/// <summary>Read-and-advance view over buffered response bytes, mirroring PipeReader.</summary>
internal interface IAsyncBufferCursor : IBufferCursor
{
	ValueTask<bool> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Read-and-advance view over buffered response bytes, mirroring PipeReader.</summary>
internal interface ISyncBufferCursor : IBufferCursor
{
	bool Read();
}
