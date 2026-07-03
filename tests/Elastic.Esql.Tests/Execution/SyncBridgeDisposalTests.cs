// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO.Pipelines;
using System.Text.Json;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

public class SyncBridgeDisposalTests
{
	private static readonly byte[] OneRowBody =
		"""{"columns":[{"name":"name","type":"keyword"},{"name":"value","type":"integer"}],"values":[["a",1]]}"""u8.ToArray();

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
	public async Task Dispose_AsyncQueryUnderSynchronizationContext_DoesNotDeadlock()
	{
		var executor = new DelayedDisposeExecutor(OneRowBody);
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync();

		var completed = RunOnThreadWithSynchronizationContext(asyncQuery.Dispose);

		completed.Should().BeTrue("Dispose must not deadlock when the calling thread has a SynchronizationContext");
		executor.LastResponse!.Disposed.Should().BeTrue();
	}

	[Test]
	public async Task Dispose_OwnedStreamUnderSynchronizationContext_DoesNotDeadlock()
	{
		var executor = new DelayedDisposeExecutor(OneRowBody);
		var stream = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToStreamAsync(EsqlFormat.Csv);

		var completed = RunOnThreadWithSynchronizationContext(stream.Dispose);

		completed.Should().BeTrue("stream disposal must not deadlock when the calling thread has a SynchronizationContext");
		executor.LastResponse!.Disposed.Should().BeTrue();
	}

	[Test]
	public async Task AsEnumerable_AsyncSubmittedQuery_ReturnsRows()
	{
		var executor = new DelayedDisposeExecutor(OneRowBody);
		var asyncQuery = await CreateExecutableQuery(executor).From("idx").AsEsqlQueryable().ToAsyncQueryAsync();

		var rows = asyncQuery.AsEnumerable().ToList();

		rows.Should().HaveCount(1);
		rows[0].Name.Should().Be("a");

		await asyncQuery.DisposeAsync();
	}

	private static bool RunOnThreadWithSynchronizationContext(Action action)
	{
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			SynchronizationContext.SetSynchronizationContext(new DroppingSynchronizationContext());
			try
			{
				action();
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		})
		{
			IsBackground = true
		};

		thread.Start();
		var completed = thread.Join(TimeSpan.FromSeconds(10));

		if (failure is not null)
			throw failure;

		return completed;
	}

	/// <summary>Drops posted continuations, mimicking a blocked UI message loop.</summary>
	private sealed class DroppingSynchronizationContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state)
		{
		}
	}

	private sealed class DelayedDisposeExecutor(byte[] body) : IEsqlQueryExecutor
	{
		public DelayedDisposeResponse? LastResponse { get; private set; }

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(string esql, EsqlParameters? parameters, object? options, EsqlFormat? format, CancellationToken cancellationToken)
		{
			LastResponse = new DelayedDisposeResponse(body);
			return Task.FromResult<IEsqlAsyncResponse>(LastResponse);
		}

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(
			string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions, EsqlFormat? format, CancellationToken cancellationToken)
		{
			LastResponse = new DelayedDisposeResponse(body);
			return Task.FromResult<IEsqlAsyncResponse>(LastResponse);
		}

		public IEsqlResponse ExecuteQuery(string esql, EsqlParameters? parameters, object? options, EsqlFormat? format) =>
			throw new NotSupportedException();

		public IEsqlResponse SubmitAsyncQuery(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions, EsqlFormat? format) =>
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

	private sealed class DelayedDisposeResponse : IEsqlAsyncResponse
	{
		private readonly Pipe _pipe = new();

		public DelayedDisposeResponse(byte[] body)
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

		public async ValueTask DisposeAsync()
		{
			// Deliberately captures the ambient SynchronizationContext (no ConfigureAwait) to model
			// transport implementations that resume on the caller's context.
			await Task.Delay(50);
			Disposed = true;
			_pipe.Reader.Complete();
		}
	}
}
