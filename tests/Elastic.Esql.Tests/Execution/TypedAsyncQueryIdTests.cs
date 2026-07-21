// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

public class TypedAsyncQueryIdTests
{
	private static readonly byte[] EmptyBody = """{"columns":[],"values":[]}"""u8.ToArray();

	private static readonly byte[] TrailingIdBody =
		"""{"columns":[{"name":"name","type":"keyword"},{"name":"value","type":"integer"}],"values":[["a",1]],"id":"trailing-id"}"""u8.ToArray();

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
	public void ToAsyncQuery_IdOnlyInResponseHeader_PopulatesQueryId()
	{
		var executor = new IdStubExecutor(EmptyBody, headerId: "header-id");
		var query = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable();

		using var asyncQuery = query.ToAsyncQuery(new EsqlAsyncQueryOptions { KeepOnCompletion = true });

		asyncQuery.QueryId.Should().Be("header-id");
	}

	[Test]
	public async Task ToAsyncQueryAsync_IdOnlyInResponseHeader_PopulatesQueryId()
	{
		var executor = new IdStubExecutor(EmptyBody, headerId: "header-id");
		var query = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable();

		await using var asyncQuery = await query.ToAsyncQueryAsync(new EsqlAsyncQueryOptions { KeepOnCompletion = true });

		asyncQuery.QueryId.Should().Be("header-id");
	}

	[Test]
	public void AsEnumerable_TrailingIdWithoutHeader_QueryIdAvailableAfterEnumeration()
	{
		var executor = new IdStubExecutor(TrailingIdBody, headerId: null);
		var query = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable();

		var asyncQuery = query.ToAsyncQuery(new EsqlAsyncQueryOptions { KeepOnCompletion = true });
		var rows = asyncQuery.AsEnumerable().ToList();

		rows.Should().HaveCount(1);
		asyncQuery.QueryId.Should().Be("trailing-id");

		asyncQuery.Dispose();
		executor.DeletedIds.Should().ContainSingle().Which.Should().Be("trailing-id");
	}

	[Test]
	public async Task AsAsyncEnumerable_TrailingIdWithoutHeader_QueryIdAvailableAfterEnumeration()
	{
		var executor = new IdStubExecutor(TrailingIdBody, headerId: null);
		var query = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable();

		var asyncQuery = await query.ToAsyncQueryAsync(new EsqlAsyncQueryOptions { KeepOnCompletion = true });

		var count = 0;
		await foreach (var _ in asyncQuery.AsAsyncEnumerable())
			count++;

		count.Should().Be(1);
		asyncQuery.QueryId.Should().Be("trailing-id");

		await asyncQuery.DisposeAsync();
		executor.DeletedIds.Should().ContainSingle().Which.Should().Be("trailing-id");
	}

	private sealed class IdStubExecutor(byte[] body, string? headerId) : IEsqlQueryExecutor
	{
		public List<string> DeletedIds { get; } = [];

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) =>
			new SyncStub(body, headerId);

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			Task.FromResult<IEsqlAsyncResponse>(new AsyncStub(body, headerId));

		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) => DeletedIds.Add(queryId);

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
		{
			DeletedIds.Add(queryId);
			return Task.CompletedTask;
		}
	}

	private sealed class SyncStub(byte[] body, string? headerId) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(body, writable: false);

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			if (name == "X-Elasticsearch-Async-Id" && headerId is not null)
			{
				values = [headerId];
				return true;
			}

			values = [];
			return false;
		}

		public void Dispose() => _stream.Dispose();
	}

	private sealed class AsyncStub : IEsqlAsyncResponse
	{
#if NET10_0_OR_GREATER
		private readonly Pipe _pipe = new();
#else
		private readonly MemoryStream _stream;
#endif
		private readonly string? _headerId;

		public AsyncStub(byte[] body, string? headerId)
		{
			_headerId = headerId;
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

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			if (name == "X-Elasticsearch-Async-Id" && _headerId is not null)
			{
				values = [_headerId];
				return true;
			}

			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
#if NET10_0_OR_GREATER
			_pipe.Reader.Complete();
#else
			_stream.Dispose();
#endif
			return ValueTask.CompletedTask;
		}
	}
}
