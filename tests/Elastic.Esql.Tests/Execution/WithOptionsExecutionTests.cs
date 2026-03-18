// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Execution;
using Elastic.Esql.Tests.Translation;

namespace Elastic.Esql.Tests.Execution;

public class WithOptionsExecutionTests : EsqlTestBase
{
	private static EsqlQueryable<T> CreateExecutableQuery<T>(CapturingQueryExecutor executor) =>
		new(new EsqlQueryProvider(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			},
			executor
		));

	[Test]
	public void WithOptions_OptionsReachExecutor_Sync()
	{
		var executor = new CapturingQueryExecutor();
		var options = new TestQueryOptions(TimeZone: "UTC");

		_ = CreateExecutableQuery<LogEntry>(executor)
			.WithOptions(options)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToList();

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQuery));
		_ = executor.Calls[0].Options.Should().BeOfType<TestQueryOptions>();
		_ = ((TestQueryOptions)executor.Calls[0].Options!).TimeZone.Should().Be("UTC");
	}

	[Test]
	public async Task WithOptions_OptionsReachExecutor_Async()
	{
		var executor = new CapturingQueryExecutor();
		var options = new TestQueryOptions(TimeZone: "America/New_York", Locale: "en-US");

		await foreach (var _ in CreateExecutableQuery<LogEntry>(executor)
			.WithOptions(options)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable()
			.AsAsyncEnumerable())
		{
		}

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		_ = executor.Calls[0].Options.Should().BeOfType<TestQueryOptions>();

		var captured = (TestQueryOptions)executor.Calls[0].Options!;
		_ = captured.TimeZone.Should().Be("America/New_York");
		_ = captured.Locale.Should().Be("en-US");
	}

	[Test]
	public void WithoutOptions_ExecutorReceivesNull()
	{
		var executor = new CapturingQueryExecutor();

		_ = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToList();

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Options.Should().BeNull();
	}

	[Test]
	public void WithOptions_EsqlStringIsCorrect()
	{
		var executor = new CapturingQueryExecutor();

		_ = CreateExecutableQuery<LogEntry>(executor)
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.Take(10)
			.ToList();

		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level == "ERROR"
			| LIMIT 10
			""".NativeLineEndings());
	}
}
