// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

internal sealed record CapturedCall(
	string Method,
	string? Esql,
	EsqlParameters? Parameters,
	EsqlQueryOptions? QueryOptions,
	object? ExecutorOptions,
	EsqlAsyncQueryOptions? AsyncOptions = null,
	EsqlFormat? Format = null);

internal sealed class CapturingQueryExecutor : IEsqlQueryExecutor
{
	/// <summary>The JSON body served for every captured call. Defaults to an empty result set.</summary>
	public string ResponseJson { get; set; } = """{"columns":[],"values":[]}""";

	public List<CapturedCall> Calls { get; } = [];

	private byte[] ResponseBytes() => Encoding.UTF8.GetBytes(ResponseJson);

	private static CapturedCall Capture(string method, EsqlExecutionRequest request) =>
		new(method, request.Esql, request.Parameters, request.QueryOptions, request.ExecutorOptions, request.AsyncOptions, request.Format);

	public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request)
	{
		Calls.Add(Capture(nameof(ExecuteQuery), request));
		return new StreamResponse(new MemoryStream(ResponseBytes()));
	}

	public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		Calls.Add(Capture(nameof(ExecuteQueryAsync), request));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(ResponseBytes()));
	}

	public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request)
	{
		Calls.Add(Capture(nameof(SubmitAsyncQuery), request));
		return new StreamResponse(new MemoryStream(ResponseBytes()));
	}

	public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		Calls.Add(Capture(nameof(SubmitAsyncQueryAsync), request));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(ResponseBytes()));
	}

	public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request)
	{
		Calls.Add(Capture(nameof(PollAsyncQuery), request));
		return new StreamResponse(new MemoryStream(ResponseBytes()));
	}

	public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		Calls.Add(Capture(nameof(PollAsyncQueryAsync), request));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(ResponseBytes()));
	}

	public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) =>
		Calls.Add(Capture(nameof(DeleteAsyncQuery), request));

	public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		Calls.Add(Capture(nameof(DeleteAsyncQueryAsync), request));
		return Task.CompletedTask;
	}

	private sealed class StreamResponse(MemoryStream stream) : IEsqlResponse
	{
		public Stream Body => stream;
		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}
		public void Dispose() => stream.Dispose();
	}

#if NET10_0_OR_GREATER
	private sealed class PipeResponse(byte[] data) : IEsqlAsyncResponse
	{
		private readonly Pipe _pipe = new();

		public PipeReader Body
		{
			get
			{
				_pipe.Writer.WriteAsync(data).AsTask().GetAwaiter().GetResult();
				_pipe.Writer.Complete();
				return _pipe.Reader;
			}
		}

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
			_pipe.Reader.Complete();
			return ValueTask.CompletedTask;
		}
	}
#else
	private sealed class PipeResponse(byte[] data) : IEsqlAsyncResponse
	{
		private readonly MemoryStream _stream = new(data, writable: false);

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
			_stream.Dispose();
			return ValueTask.CompletedTask;
		}
	}
#endif
}
