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
		var response = new TrackingResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_CalledTwice_DisposesResponseOnce()
	{
		var response = new TrackingResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();
		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task Complete_AfterCompleteAsync_DoesNotDisposeAgain()
	{
		var response = new TrackingResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();
		reader.Complete();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_AfterComplete_DoesNotDisposeAgain()
	{
		var response = new TrackingResponse("data"u8.ToArray());
		var reader = new OwnedAsyncResponsePipeReader(response);

		reader.Complete();
		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task CompleteAsync_ResponseDisposesAsynchronously_Completes()
	{
		var response = new TrackingResponse("data"u8.ToArray()) { DisposeDelay = TimeSpan.FromMilliseconds(10) };
		var reader = new OwnedAsyncResponsePipeReader(response);

		await reader.CompleteAsync();

		response.DisposeCount.Should().Be(1);
	}

	private sealed class TrackingResponse : IEsqlAsyncResponse
	{
		private readonly Pipe _pipe = new();
		private int _disposeCount;

		public TrackingResponse(byte[] data)
		{
			_pipe.Writer.WriteAsync(data).AsTask().GetAwaiter().GetResult();
			_pipe.Writer.Complete();
		}

		public TimeSpan DisposeDelay { get; init; }

		public int DisposeCount => _disposeCount;

		public PipeReader Body => _pipe.Reader;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public async ValueTask DisposeAsync()
		{
			if (DisposeDelay > TimeSpan.Zero)
				await Task.Delay(DisposeDelay);

			Interlocked.Increment(ref _disposeCount);
			_pipe.Reader.Complete();
		}
	}
}

#endif
