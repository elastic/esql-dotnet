// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO.Pipelines;
using System.Text;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

internal sealed record CapturedCall(string Method, string? Esql, EsqlParameters? Parameters, object? Options, EsqlAsyncQueryOptions? AsyncOptions = null);

internal sealed class CapturingQueryExecutor : IEsqlQueryExecutor
{
	private static readonly byte[] EmptyResponse = Encoding.UTF8.GetBytes("""{"columns":[],"values":[]}""");

	public List<CapturedCall> Calls { get; } = [];

	public IEsqlResponse ExecuteQuery(string esql, EsqlParameters? parameters, object? options)
	{
		Calls.Add(new CapturedCall(nameof(ExecuteQuery), esql, parameters, options));
		return new StreamResponse(new MemoryStream(EmptyResponse));
	}

	public Task<IEsqlAsyncResponse> ExecuteQueryAsync(string esql, EsqlParameters? parameters, object? options, CancellationToken cancellationToken)
	{
		Calls.Add(new CapturedCall(nameof(ExecuteQueryAsync), esql, parameters, options));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(EmptyResponse));
	}

	public IEsqlResponse SubmitAsyncQuery(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions)
	{
		Calls.Add(new CapturedCall(nameof(SubmitAsyncQuery), esql, parameters, options, asyncOptions));
		return new StreamResponse(new MemoryStream(EmptyResponse));
	}

	public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions, CancellationToken cancellationToken)
	{
		Calls.Add(new CapturedCall(nameof(SubmitAsyncQueryAsync), esql, parameters, options, asyncOptions));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(EmptyResponse));
	}

	public IEsqlResponse PollAsyncQuery(string queryId, object? options)
	{
		Calls.Add(new CapturedCall(nameof(PollAsyncQuery), null, null, options));
		return new StreamResponse(new MemoryStream(EmptyResponse));
	}

	public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken)
	{
		Calls.Add(new CapturedCall(nameof(PollAsyncQueryAsync), null, null, options));
		return Task.FromResult<IEsqlAsyncResponse>(new PipeResponse(EmptyResponse));
	}

	public void DeleteAsyncQuery(string queryId, object? options) =>
		Calls.Add(new CapturedCall(nameof(DeleteAsyncQuery), null, null, options));

	public Task DeleteAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken)
	{
		Calls.Add(new CapturedCall(nameof(DeleteAsyncQueryAsync), null, null, options));
		return Task.CompletedTask;
	}

	private sealed class StreamResponse(MemoryStream stream) : IEsqlResponse
	{
		public Stream Body => stream;
		public void Dispose() => stream.Dispose();
	}

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

		public ValueTask DisposeAsync()
		{
			_pipe.Reader.Complete();
			return ValueTask.CompletedTask;
		}
	}
}
