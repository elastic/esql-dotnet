// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using Elastic.Esql.Execution;

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
	public void ToAsyncQuery_MalformedResponseBodyAndThrowingDispose_SurfacesOriginalException()
	{
		var executor = new TrackingExecutor("""{"unexpected":true}"""u8.ToArray(), throwOnDispose: true);
		var query = CreateExecutableQuery(executor).From("logs-*").AsEsqlQueryable();

		var act = () => query.ToAsyncQuery();

		act.Should().Throw<JsonException>();
		executor.LastSyncResponse!.Disposed.Should().BeTrue();
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

	private sealed class TrackingExecutor(byte[] submitBody, bool throwOnDispose = false) : IEsqlQueryExecutor
	{
		public TrackingSyncResponse? LastSyncResponse { get; private set; }
		public TrackingAsyncResponse? LastAsyncResponse { get; private set; }

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request)
		{
			LastSyncResponse = new TrackingSyncResponse(submitBody, throwOnDispose);
			return LastSyncResponse;
		}

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken)
		{
			LastAsyncResponse = new TrackingAsyncResponse(submitBody);
			return Task.FromResult<IEsqlAsyncResponse>(LastAsyncResponse);
		}

		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request)
		{
		}

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}

	private sealed class TrackingSyncResponse(byte[] body, bool throwOnDispose = false) : IEsqlResponse
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

			if (throwOnDispose)
				throw new InvalidOperationException("Simulated dispose failure.");
		}
	}

	private sealed class TrackingAsyncResponse : IEsqlAsyncResponse
	{
#if NET10_0_OR_GREATER
		private readonly Pipe _pipe = new();
#else
		private readonly MemoryStream _stream;
#endif

		public TrackingAsyncResponse(byte[] body)
		{
#if NET10_0_OR_GREATER
			_pipe.Writer.WriteAsync(body).AsTask().GetAwaiter().GetResult();
			_pipe.Writer.Complete();
#else
			_stream = new MemoryStream(body, writable: false);
#endif
		}

		public bool Disposed { get; private set; }

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

		public ValueTask DisposeAsync()
		{
			Disposed = true;
#if NET10_0_OR_GREATER
			_pipe.Reader.Complete();
#else
			_stream.Dispose();
#endif
			return ValueTask.CompletedTask;
		}
	}
}
