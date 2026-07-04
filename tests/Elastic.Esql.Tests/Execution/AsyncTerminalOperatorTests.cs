// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

public class AsyncTerminalOperatorTests : EsqlTestBase
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
	public async Task ToListAsync_TwoRows_MaterializesRowsAndCapturesEsql()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"},{"name":"statusCode","type":"integer"}],"values":[["a",200],["b",500]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable()
			.ToListAsync();

		_ = result.Should().HaveCount(2);
		_ = result[0].Message.Should().Be("a");
		_ = result[0].StatusCode.Should().Be(200);
		_ = result[1].Message.Should().Be("b");
		_ = result[1].StatusCode.Should().Be(500);
		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level == "ERROR"
			""".NativeLineEndings());
	}

	[Test]
	public async Task ToArrayAsync_TwoRows_ReturnsArray()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"}],"values":[["a"],["b"]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.ToArrayAsync();

		_ = result.Should().HaveCount(2);
		_ = result[1].Message.Should().Be("b");
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		_ = executor.Calls[0].Esql.Should().Be("FROM logs-*");
	}

	[Test]
	public async Task FirstAsync_OneRow_ReturnsElementAndAppendsLimitOne()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"}],"values":[["hello"]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.FirstAsync();

		_ = result.Message.Should().Be("hello");
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 1
			""".NativeLineEndings());
	}

	[Test]
	public async Task FirstAsync_NoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor();

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.FirstAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Sequence contains no elements");
	}

	[Test]
	public async Task FirstOrDefaultAsync_NoRows_ReturnsDefaultAndAppendsLimitOne()
	{
		var executor = new CapturingQueryExecutor();

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.FirstOrDefaultAsync();

		_ = result.Should().BeNull();
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 1
			""".NativeLineEndings());
	}

	[Test]
	public async Task SingleAsync_OneRow_ReturnsElementAndAppendsLimitTwo()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"}],"values":[["only"]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.SingleAsync();

		_ = result.Message.Should().Be("only");
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 2
			""".NativeLineEndings());
	}

	[Test]
	public async Task SingleAsync_NoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor();

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.SingleAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Sequence contains no elements");
	}

	[Test]
	public async Task SingleAsync_TwoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"}],"values":[["a"],["b"]]}"""
		};

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.SingleAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Sequence contains more than one element");
	}

	[Test]
	public async Task SingleOrDefaultAsync_NoRows_ReturnsDefault()
	{
		var executor = new CapturingQueryExecutor();

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.SingleOrDefaultAsync();

		_ = result.Should().BeNull();
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 2
			""".NativeLineEndings());
	}

	[Test]
	public async Task SingleOrDefaultAsync_TwoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"message","type":"keyword"}],"values":[["a"],["b"]]}"""
		};

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.SingleOrDefaultAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Sequence contains more than one element");
	}

	[Test]
	public async Task CountAsync_SingleRow_ReturnsScalarAndAppendsStats()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"count","type":"long"}],"values":[[42]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.CountAsync();

		_ = result.Should().Be(42);
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| STATS count = COUNT(*)
			""".NativeLineEndings());
	}

	[Test]
	public async Task CountAsync_NoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor();

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.CountAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Operation 'CountAsync' expected exactly one row but got 0");
	}

	[Test]
	public async Task AnyAsync_TrueRow_ReturnsTrueAndAppendsStatsEval()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"result","type":"boolean"}],"values":[[true]]}"""
		};

		var result = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.AnyAsync();

		_ = result.Should().BeTrue();
		_ = executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| STATS result = COUNT(*)
			| EVAL result = result > 0
			""".NativeLineEndings());
	}

	[Test]
	public async Task AnyAsync_NoRows_ThrowsInvalidOperationException()
	{
		var executor = new CapturingQueryExecutor();

		var act = async () => await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.AnyAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Operation 'AnyAsync' expected exactly one row but got 0");
	}
}
