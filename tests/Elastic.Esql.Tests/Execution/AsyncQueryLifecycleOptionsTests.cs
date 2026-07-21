// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

public class AsyncQueryLifecycleOptionsTests
{
	private static EsqlQueryable<LogEntry> CreateExecutableQuery(CapturingQueryExecutor executor) =>
		new(new EsqlQueryProvider(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			},
			executor
		));

	[Test]
	public void SubmitRefreshDispose_PassesSubmissionOptionsToPollAndDelete()
	{
		var executor = new CapturingQueryExecutor
		{
			// "id" and "is_running" precede "columns" so both are captured on the submit response;
			// is_running: true keeps the query open so Refresh() and Dispose() issue poll and delete.
			ResponseJson = """{"id":"query-1","is_running":true,"columns":[],"values":[]}"""
		};
		var queryOptions = new EsqlQueryOptions { DropNullColumns = true, TimeZone = "UTC" };
		var asyncOptions = new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromMinutes(5) };

		var query = CreateExecutableQuery(executor)
			.WithOptions(queryOptions)
			.From("logs-*")
			.AsEsqlQueryable();

		using (var asyncQuery = query.ToAsyncQuery(asyncOptions))
			asyncQuery.Refresh();

		var poll = executor.Calls.Single(c => c.Method == nameof(IEsqlQueryExecutor.PollAsyncQuery));
		_ = poll.QueryOptions.Should().BeSameAs(queryOptions);
		_ = poll.AsyncOptions.Should().BeSameAs(asyncOptions);

		var delete = executor.Calls.Single(c => c.Method == nameof(IEsqlQueryExecutor.DeleteAsyncQuery));
		_ = delete.QueryOptions.Should().BeSameAs(queryOptions);
		_ = delete.AsyncOptions.Should().BeSameAs(asyncOptions);
	}

	[Test]
	public async Task SubmitRefreshAsyncDisposeAsync_PassesSubmissionOptionsToPollAndDelete()
	{
		var executor = new CapturingQueryExecutor
		{
			// "id" and "is_running" precede "columns" so both are captured on the submit response;
			// is_running: true keeps the query open so RefreshAsync() and DisposeAsync() issue poll and delete.
			ResponseJson = """{"id":"query-1","is_running":true,"columns":[],"values":[]}"""
		};
		var queryOptions = new EsqlQueryOptions { DropNullColumns = true, TimeZone = "UTC" };
		var asyncOptions = new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromMinutes(5) };

		var query = CreateExecutableQuery(executor)
			.WithOptions(queryOptions)
			.From("logs-*")
			.AsEsqlQueryable();

		await using (var asyncQuery = await query.ToAsyncQueryAsync(asyncOptions))
			await asyncQuery.RefreshAsync();

		var poll = executor.Calls.Single(c => c.Method == nameof(IEsqlQueryExecutor.PollAsyncQueryAsync));
		_ = poll.QueryOptions.Should().BeSameAs(queryOptions);
		_ = poll.AsyncOptions.Should().BeSameAs(asyncOptions);

		var delete = executor.Calls.Single(c => c.Method == nameof(IEsqlQueryExecutor.DeleteAsyncQueryAsync));
		_ = delete.QueryOptions.Should().BeSameAs(queryOptions);
		_ = delete.AsyncOptions.Should().BeSameAs(asyncOptions);
	}
}
