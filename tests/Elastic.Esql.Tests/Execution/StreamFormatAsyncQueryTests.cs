// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO.Pipelines;
using System.Text;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

public class StreamFormatAsyncQueryTests
{
	private static EsqlExecutionRequest Request(EsqlFormat format) =>
		new() { Esql = "FROM test", Format = format };

	[Test]
	public void Submit_PopulatesQueryIdAndIsRunningFromHeaders()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));

		q.QueryId.Should().Be("abc123");
		q.IsRunning.Should().BeTrue();
		q.IsCompleted.Should().BeFalse();
	}

	[Test]
	public void GetResponseStream_BeforeCompletion_Throws()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));

		var act = () => q.GetResponseStream();
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*has not completed*");
	}

	[Test]
	public void Refresh_UpdatesIsRunningFromPollHeaders()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: false, body: "a,b,c\n1,2,3"u8.ToArray());

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));
		q.IsRunning.Should().BeTrue();

		q.Refresh();

		q.IsRunning.Should().BeFalse();
		q.IsCompleted.Should().BeTrue();
	}

	[Test]
	public void WaitForCompletion_PollsUntilNotRunning()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: false, body: "ok"u8.ToArray());

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));

		q.WaitForCompletion(TimeSpan.FromMilliseconds(1));

		q.IsCompleted.Should().BeTrue();
		executor.PollCount.Should().Be(3);
	}

	[Test]
	public async Task WaitForCompletionAsync_PollsUntilNotRunning()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: false, body: "ok"u8.ToArray());

		await using var q = new EsqlAsyncQuery(executor, executor.NextAsyncSubmit(), Request(EsqlFormat.Arrow));

		await q.WaitForCompletionAsync(TimeSpan.FromMilliseconds(1));

		q.IsCompleted.Should().BeTrue();
		executor.PollCount.Should().Be(2);
	}

	[Test]
	public void GetResponseStream_AfterCompletion_ReturnsBody()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: true, body: []);
		executor.QueuePoll(id: "abc123", isRunning: false, body: "a,b\n1,2"u8.ToArray());

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));
		q.WaitForCompletion(TimeSpan.FromMilliseconds(1));

		using var stream = q.GetResponseStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		reader.ReadToEnd().Should().Be("a,b\n1,2");
	}

	[Test]
	public async Task DisposeAsync_IssuesDelete()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc123", isRunning: false, body: "data"u8.ToArray());

		var q = new EsqlAsyncQuery(executor, executor.NextAsyncSubmit(), Request(EsqlFormat.Csv));
		await q.DisposeAsync();

		executor.DeletedIds.Should().ContainSingle().Which.Should().Be("abc123");
	}

	[Test]
	public void Dispose_NoQueryId_DoesNotCallDelete()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: null, isRunning: false, body: "data"u8.ToArray());

		var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));
		q.Dispose();

		executor.DeletedIds.Should().BeEmpty();
	}

	[Test]
	public void Submit_NotRunning_AllowsImmediateReadWithoutPoll()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: null, isRunning: false, body: "csv data here"u8.ToArray());

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Csv));

		q.IsCompleted.Should().BeTrue();
		executor.PollCount.Should().Be(0);

		using var stream = q.GetResponseStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		reader.ReadToEnd().Should().Be("csv data here");
	}

	[Test]
	public void Refresh_PassesFormatToExecutor()
	{
		var executor = new StubRawExecutor();
		executor.QueueSubmit(id: "abc", isRunning: true, body: []);
		executor.QueuePoll(id: "abc", isRunning: false, body: []);

		using var q = new EsqlAsyncQuery(executor, executor.NextSyncSubmit(), Request(EsqlFormat.Arrow));
		q.Refresh();

		executor.LastPollFormat.Should().Be(EsqlFormat.Arrow);
	}

	private sealed class StubRawExecutor : IEsqlQueryExecutor
	{
		private readonly Queue<StubResponse> _submitQueue = new();
		private readonly Queue<StubResponse> _pollQueue = new();
		public List<string> DeletedIds { get; } = [];
		public int PollCount { get; private set; }
		public EsqlFormat? LastPollFormat { get; private set; }

		public void QueueSubmit(string? id, bool isRunning, byte[] body) =>
			_submitQueue.Enqueue(new StubResponse(id, isRunning, body));

		public void QueuePoll(string? id, bool isRunning, byte[] body) =>
			_pollQueue.Enqueue(new StubResponse(id, isRunning, body));

		public IEsqlResponse NextSyncSubmit() => new SyncStub(_submitQueue.Dequeue());
		public IEsqlAsyncResponse NextAsyncSubmit() => new AsyncStub(_submitQueue.Dequeue());

		public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request) =>
			throw new NotSupportedException();

		public Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken ct) =>
			throw new NotSupportedException();

		public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request) =>
			NextSyncSubmit();

		public Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken ct) =>
			Task.FromResult(NextAsyncSubmit());

		public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request)
		{
			PollCount++;
			LastPollFormat = request.Format;
			return new SyncStub(_pollQueue.Dequeue());
		}

		public Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken ct)
		{
			PollCount++;
			LastPollFormat = request.Format;
			return Task.FromResult<IEsqlAsyncResponse>(new AsyncStub(_pollQueue.Dequeue()));
		}

		public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request) => DeletedIds.Add(queryId);

		public Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken ct)
		{
			DeletedIds.Add(queryId);
			return Task.CompletedTask;
		}
	}

	private sealed record StubResponse(string? Id, bool IsRunning, byte[] Body);

	private sealed class SyncStub(StubResponse data) : IEsqlResponse
	{
		private readonly MemoryStream _stream = new(data.Body, writable: false);

		public Stream Body => _stream;

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			if (name == "X-Elasticsearch-Async-Id" && data.Id is not null)
			{
				values = [data.Id];
				return true;
			}

			if (name == "X-Elasticsearch-Async-Is-Running")
			{
				values = [data.IsRunning ? "true" : "false"];
				return true;
			}

			values = [];
			return false;
		}

		public void Dispose() => _stream.Dispose();
	}

	private sealed class AsyncStub(StubResponse data) : IEsqlAsyncResponse
	{
		private readonly Pipe _pipe = new();

		public PipeReader Body
		{
			get
			{
				_pipe.Writer.WriteAsync(data.Body).AsTask().GetAwaiter().GetResult();
				_pipe.Writer.Complete();
				return _pipe.Reader;
			}
		}

		public bool TryGetHeader(string name, out IEnumerable<string> values)
		{
			if (name == "X-Elasticsearch-Async-Id" && data.Id is not null)
			{
				values = [data.Id];
				return true;
			}

			if (name == "X-Elasticsearch-Async-Is-Running")
			{
				values = [data.IsRunning ? "true" : "false"];
				return true;
			}

			values = [];
			return false;
		}

		public ValueTask DisposeAsync()
		{
			_pipe.Reader.Complete();
			return ValueTask.CompletedTask;
		}
	}
}
