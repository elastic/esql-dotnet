// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// OwnedAsyncResponsePipeReader is a net10-only source type (PipeReader-based), so its tests only apply there.
#if NET10_0_OR_GREATER

using System.IO.Pipelines;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

public class OwnedPipeReaderCompletionTests
{
	[Test]
	public async Task CompleteAsync_FirstCall_DisposesResponseOnce()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_CalledTwice_DisposesResponseOnce()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();
		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task Complete_AfterCompleteAsync_DoesNotDisposeAgain()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();
		reader.Complete();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_AfterComplete_DoesNotDisposeAgain()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		reader.Complete();
		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_ResponseDisposesAsynchronously_Completes()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray()) { DisposeDelay = TimeSpan.FromMilliseconds(10) };
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public void Complete_InnerCompletionThrows_DisposesResponseAndRethrows()
	{
		var response = new ThrowingCompletionResponse();
		var reader = new OwnedAsyncResponsePipeReader(response);

		var act = () => reader.Complete();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("Simulated completion failure.");
		_ = response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_InnerCompletionFaults_DisposesResponseAndRethrows()
	{
		var response = new ThrowingCompletionResponse();
		var reader = new OwnedAsyncResponsePipeReader(response);

		var act = async () => await reader.CompleteAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated completion failure.");
		_ = response.DisposeCount.Should().Be(1);
	}

	private sealed class ThrowingCompletionResponse : IEsqlAsyncResponse
	{
		private int _disposeCount;

		public int DisposeCount => _disposeCount;

		public PipeReader Body { get; } = new ThrowingCompletionPipeReader();

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
			_ = Interlocked.Increment(ref _disposeCount);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ThrowingCompletionPipeReader : PipeReader
	{
		public override void AdvanceTo(SequencePosition consumed)
		{
		}

		public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
		{
		}

		public override void CancelPendingRead()
		{
		}

		public override void Complete(Exception? exception = null) =>
			throw new InvalidOperationException("Simulated completion failure.");

		public override ValueTask CompleteAsync(Exception? exception = null) =>
			ValueTask.FromException(new InvalidOperationException("Simulated completion failure."));

		public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
			new(new ReadResult(default, isCanceled: false, isCompleted: true));

		public override bool TryRead(out ReadResult result)
		{
			result = new ReadResult(default, isCanceled: false, isCompleted: true);
			return true;
		}
	}
}

#endif
