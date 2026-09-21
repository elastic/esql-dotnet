// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text.Json;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

/// <summary>The server-side query must be deleted even when releasing the local response fails.</summary>
public class AsyncQueryDisposeFailureTests
{
	private static readonly byte[] EmptyBody = """{"columns":[],"values":[]}"""u8.ToArray();
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
	public void TypedDispose_ResponseDisposeThrows_StillDeletesQueryAndRethrows()
	{
		var executor = new ThrowingDisposeExecutor(EmptyBody);
		var asyncQuery = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQuery(KeepOptions);

		var act = () => asyncQuery.Dispose();

		_ = act.Should().Throw<InvalidOperationException>();
		_ = executor.DeletedIds.Should().ContainSingle().Which.Should().Be("q-1");
	}

	[Test]
	public async Task TypedDisposeAsync_ResponseDisposeThrows_StillDeletesQueryAndRethrows()
	{
		var executor = new ThrowingDisposeExecutor(EmptyBody);
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync(KeepOptions);

		var act = async () => await asyncQuery.DisposeAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>();
		_ = executor.DeletedIds.Should().ContainSingle().Which.Should().Be("q-1");
	}

	[Test]
	public void RawDispose_ResponseDisposeThrows_StillDeletesQueryAndRethrows()
	{
		var executor = new ThrowingDisposeExecutor(EmptyBody);
		var asyncQuery = CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQuery(EsqlFormat.Csv, KeepOptions);

		var act = () => asyncQuery.Dispose();

		_ = act.Should().Throw<InvalidOperationException>();
		_ = executor.DeletedIds.Should().ContainSingle().Which.Should().Be("q-1");
	}

	[Test]
	public async Task RawDisposeAsync_ResponseDisposeThrows_StillDeletesQueryAndRethrows()
	{
		var executor = new ThrowingDisposeExecutor(EmptyBody);
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync(EsqlFormat.Csv, KeepOptions);

		var act = async () => await asyncQuery.DisposeAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>();
		_ = executor.DeletedIds.Should().ContainSingle().Which.Should().Be("q-1");
	}

	private sealed class ThrowingDisposeExecutor(byte[] body) : IEsqlQueryExecutor
	{
		public List<string> DeletedIds { get; } = [];

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) =>
			new ThrowingSyncResponse(body);

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			Task.FromResult<IEsqlAsyncResponse>(new ThrowingAsyncResponse(body));

		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) =>
			DeletedIds.Add(queryId);

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
		{
			DeletedIds.Add(queryId);
			return Task.CompletedTask;
		}
	}

	private static bool TryGetAsyncIdHeader(string name, out IEnumerable<string> values)
	{
		if (name == "X-Elasticsearch-Async-Id")
		{
			values = ["q-1"];
			return true;
		}

		values = [];
		return false;
	}

	private sealed class ThrowingSyncResponse(byte[] body) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(body, writable: false);

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values) =>
			TryGetAsyncIdHeader(name, out values);

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
			TryGetAsyncIdHeader(name, out values);

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
