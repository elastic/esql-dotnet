// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

/// <summary>Test fake that counts how many times <see cref="DisposeAsync"/> is called.</summary>
internal sealed class CountingAsyncResponse : IEsqlAsyncResponse
{
#if NET10_0_OR_GREATER
	private readonly Pipe _pipe = new();
#else
	private readonly MemoryStream _stream;
#endif
	private int _disposeCount;

	public CountingAsyncResponse(byte[] data)
	{
#if NET10_0_OR_GREATER
		_pipe.Writer.WriteAsync(data).AsTask().GetAwaiter().GetResult();
		_pipe.Writer.Complete();
#else
		_stream = new MemoryStream(data, writable: false);
#endif
	}

	public TimeSpan DisposeDelay { get; init; }

	public int DisposeCount => _disposeCount;

#if NET10_0_OR_GREATER
	public PipeReader Body => _pipe.Reader;
#else
	public Stream Body => _stream;
#endif

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
#if NET10_0_OR_GREATER
		_pipe.Reader.Complete();
#else
		_stream.Dispose();
#endif
	}
}
