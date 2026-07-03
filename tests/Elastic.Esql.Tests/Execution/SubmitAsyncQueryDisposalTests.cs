// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO.Pipelines;
using System.Text.Json;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

public class SubmitAsyncQueryDisposalTests
{
	private static EsqlQueryable<LogEntry> CreateExecutableQuery(IEsqlQueryExecutor executor) =>
		new(new EsqlQueryProvider(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			},
			executor
		));

	[Test]
	public void ToAsyncQuery_MalformedResponseBody_DisposesTransportResponse()
	{
		var executor = new TrackingExecutor("""{"unexpected":true}"""u8.ToArray());
		var query = CreateExecutableQuery(executor).From("logs-*").AsEsqlQueryable();

		var act = () => query.ToAsyncQuery();

		act.Should().Throw<JsonException>();
		executor.LastSyncResponse!.Disposed.Should().BeTrue();
	}

	[Test]
	public async Task ToAsyncQueryAsync_MalformedResponseBody_DisposesTransportResponse()
	{
		var executor = new TrackingExecutor("""{"unexpected":true}"""u8.ToArray());
		var query = CreateExecutableQuery(executor).From("logs-*").AsEsqlQueryable();

		var act = () => query.ToAsyncQueryAsync();

		await act.Should().ThrowAsync<JsonException>();
		executor.LastAsyncResponse!.Disposed.Should().BeTrue();
	}

	[Test]
	public void ToAsyncQuery_WellFormedResponseBody_KeepsTransportResponseOpen()
	{
		var executor = new TrackingExecutor("""{"columns":[],"values":[]}"""u8.ToArray());
		var query = CreateExecutableQuery(executor).From("logs-*").AsEsqlQueryable();

		using var asyncQuery = query.ToAsyncQuery();

		executor.LastSyncResponse!.Disposed.Should().BeFalse();
	}

	[Test]
	public async Task ToAsyncQueryAsync_WellFormedResponseBody_KeepsTransportResponseOpen()
	{
		var executor = new TrackingExecutor("""{"columns":[],"values":[]}"""u8.ToArray());
		var query = CreateExecutableQuery(executor).From("logs-*").AsEsqlQueryable();

		await using var asyncQuery = await query.ToAsyncQueryAsync();

		executor.LastAsyncResponse!.Disposed.Should().BeFalse();
	}

	private sealed class TrackingExecutor(byte[] submitBody) : IEsqlQueryExecutor
	{
		public TrackingSyncResponse? LastSyncResponse { get; private set; }
		public TrackingAsyncResponse? LastAsyncResponse { get; private set; }

		public IEsqlResponse SubmitAsyncQuery(
			string esql,
			EsqlParameters? parameters,
			object? options,
			EsqlAsyncQueryOptions? asyncOptions,
			EsqlFormat? format
		)
		{
			LastSyncResponse = new TrackingSyncResponse(submitBody);
			return LastSyncResponse;
		}

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(
			string esql,
			EsqlParameters? parameters,
			object? options,
			EsqlAsyncQueryOptions? asyncOptions,
			EsqlFormat? format,
			CancellationToken cancellationToken
		)
		{
			LastAsyncResponse = new TrackingAsyncResponse(submitBody);
			return Task.FromResult<IEsqlAsyncResponse>(LastAsyncResponse);
		}

		public IEsqlResponse ExecuteQuery(string esql, EsqlParameters? parameters, object? options, EsqlFormat? format) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(
			string esql,
			EsqlParameters? parameters,
			object? options,
			EsqlFormat? format,
			CancellationToken cancellationToken
		) =>
			throw new NotSupportedException();

		public IEsqlResponse PollAsyncQuery(string queryId, object? options, EsqlFormat? format) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, object? options, EsqlFormat? format, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public void DeleteAsyncQuery(string queryId, object? options)
		{
		}

		public Task DeleteAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}

	private sealed class TrackingSyncResponse(byte[] body) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(body, writable: false);

		public bool Disposed { get; private set; }

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public void Dispose()
		{
			Disposed = true;
			_stream.Dispose();
		}
	}

	private sealed class TrackingAsyncResponse : IEsqlAsyncResponse
	{
		private readonly Pipe _pipe = new();

		public TrackingAsyncResponse(byte[] body)
		{
			_pipe.Writer.WriteAsync(body).AsTask().GetAwaiter().GetResult();
			_pipe.Writer.Complete();
		}

		public bool Disposed { get; private set; }

		public PipeReader Body => _pipe.Reader;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			_pipe.Reader.Complete();
			return ValueTask.CompletedTask;
		}
	}
}
