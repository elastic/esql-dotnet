// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

/// <summary>A polled response must not leak when releasing the previous response throws.</summary>
public class AsyncQueryPollHandoffTests
{
	// Metadata leads the rows so the typed reader knows the query is still running when the query is constructed.
	private static readonly byte[] RunningBody = """{"id":"q-1","is_running":true,"columns":[],"values":[]}"""u8.ToArray();
	private static readonly byte[] CompletedBody = """{"id":"q-1","is_running":false,"columns":[],"values":[]}"""u8.ToArray();
	private static readonly EsqlAsyncQueryOptions KeepOptions = new() { KeepOnCompletion = true };

	private static EsqlQueryable<SimpleDocument> CreateExecutableQuery(IEsqlQueryExecutor executor) =>
		new(new EsqlQueryProvider(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			},
			executor
		));

	[Test]
	public void TypedRefresh_PreviousResponseDisposeThrows_DisposesPolledResponseAndRethrows()
	{
		var executor = new HandoffExecutor();
		var asyncQuery = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQuery(KeepOptions);

		var act = () => asyncQuery.Refresh();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("Simulated dispose failure.");
		_ = executor.PolledSyncResponse.Should().NotBeNull();
		_ = executor.PolledSyncResponse.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task TypedRefreshAsync_PreviousResponseDisposeThrows_DisposesPolledResponseAndRethrows()
	{
		var executor = new HandoffExecutor();
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync(KeepOptions);

		var act = async () => await asyncQuery.RefreshAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated dispose failure.");
		_ = executor.PolledAsyncResponse.Should().NotBeNull();
		_ = executor.PolledAsyncResponse.DisposeCount.Should().Be(1);
	}

	[Test]
	public void RawRefresh_PreviousResponseDisposeThrows_DisposesPolledResponseAndRethrows()
	{
		var executor = new HandoffExecutor();
		var asyncQuery = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQuery(EsqlFormat.Csv, KeepOptions);

		var act = () => asyncQuery.Refresh();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("Simulated dispose failure.");
		_ = executor.PolledSyncResponse.Should().NotBeNull();
		_ = executor.PolledSyncResponse.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task RawRefreshAsync_PreviousResponseDisposeThrows_DisposesPolledResponseAndRethrows()
	{
		var executor = new HandoffExecutor();
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync(EsqlFormat.Csv, KeepOptions);

		var act = async () => await asyncQuery.RefreshAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated dispose failure.");
		_ = executor.PolledAsyncResponse.Should().NotBeNull();
		_ = executor.PolledAsyncResponse.DisposeCount.Should().Be(1);
	}

	private static bool TryGetRunningQueryHeader(string name, out IEnumerable<string> values)
	{
		switch (name)
		{
			case "X-Elasticsearch-Async-Id":
				values = ["q-1"];
				return true;
			case "X-Elasticsearch-Async-Is-Running":
				values = ["?1"];
				return true;
			default:
				values = [];
				return false;
		}
	}

	/// <summary>Submits responses whose disposal throws and answers polls with counting responses.</summary>
	private sealed class HandoffExecutor : IEsqlQueryExecutor
	{
		public CountingSyncResponse? PolledSyncResponse { get; private set; }

		public CountingAsyncResponse? PolledAsyncResponse { get; private set; }

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) =>
			new ThrowingSyncResponse(RunningBody);

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			Task.FromResult<IEsqlAsyncResponse>(new ThrowingAsyncResponse(RunningBody));

		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request)
		{
			PolledSyncResponse = new CountingSyncResponse(CompletedBody);
			return PolledSyncResponse;
		}

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
		{
			PolledAsyncResponse = new CountingAsyncResponse(CompletedBody);
			return Task.FromResult<IEsqlAsyncResponse>(PolledAsyncResponse);
		}

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request)
		{
		}

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			Task.CompletedTask;
	}

	private sealed class CountingSyncResponse(byte[] body) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(body, writable: false);

		public int DisposeCount { get; private set; }

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			values = [];
			return false;
		}

		public void Dispose()
		{
			DisposeCount++;
			_stream.Dispose();
		}
	}

	private sealed class ThrowingSyncResponse(byte[] body) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(body, writable: false);

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values) =>
			TryGetRunningQueryHeader(name, out values);

		public void Dispose()
		{
			_stream.Dispose();
			throw new InvalidOperationException("Simulated dispose failure.");
		}
	}

	private sealed class ThrowingAsyncResponse : IEsqlAsyncResponse
	{
#if NET10_0_OR_GREATER
		private readonly Pipe _pipe = new();
#else
		private readonly MemoryStream _stream;
#endif

		public ThrowingAsyncResponse(byte[] body)
		{
#if NET10_0_OR_GREATER
			_pipe.Writer.WriteAsync(body).AsTask().GetAwaiter().GetResult();
			_pipe.Writer.Complete();
#else
			_stream = new MemoryStream(body, writable: false);
#endif
		}

#if NET10_0_OR_GREATER
		public PipeReader Body => _pipe.Reader;
#else
		public Stream Body => _stream;
#endif

		public bool TryGetHeader(string name, out IEnumerable<string> values) =>
			TryGetRunningQueryHeader(name, out values);

		public ValueTask DisposeAsync()
		{
#if NET10_0_OR_GREATER
			_pipe.Reader.Complete();
#else
			_stream.Dispose();
#endif
			throw new InvalidOperationException("Simulated dispose failure.");
		}
	}
}
