// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using Elastic.Esql.Tests.Translation;

namespace Elastic.Esql.Tests.Execution;

public class StreamFormatTranslationTests : EsqlTestBase
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
	[Arguments(EsqlFormat.Json, "json")]
	[Arguments(EsqlFormat.Csv, "csv")]
	[Arguments(EsqlFormat.Tsv, "tsv")]
	[Arguments(EsqlFormat.Txt, "txt")]
	[Arguments(EsqlFormat.Arrow, "arrow")]
	[Arguments(EsqlFormat.Smile, "smile")]
	[Arguments(EsqlFormat.Cbor, "cbor")]
	[Arguments(EsqlFormat.Yaml, "yaml")]
	public void GetFormatName_ReturnsWireIdentifier(EsqlFormat format, string expected) =>
		format.GetFormatName().Should().Be(expected);

	[Test]
	[Arguments(EsqlFormat.Json, "application/json")]
	[Arguments(EsqlFormat.Csv, "text/csv")]
	[Arguments(EsqlFormat.Tsv, "text/tab-separated-values")]
	[Arguments(EsqlFormat.Txt, "text/plain")]
	[Arguments(EsqlFormat.Arrow, "application/vnd.apache.arrow.stream")]
	[Arguments(EsqlFormat.Smile, "application/smile")]
	[Arguments(EsqlFormat.Cbor, "application/cbor")]
	[Arguments(EsqlFormat.Yaml, "application/yaml")]
	public void GetMediaType_ReturnsHttpAcceptValue(EsqlFormat format, string expected) =>
		format.GetMediaType().Should().Be(expected);

	[Test]
	public void ToStream_PropagatesFormatToExecutor()
	{
		var executor = new CapturingQueryExecutor();

		using var _ = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable()
			.ToStream(EsqlFormat.Csv);

		executor.Calls.Should().HaveCount(1);
		executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQuery));
		executor.Calls[0].Format.Should().Be(EsqlFormat.Csv);
	}

	[Test]
	public async Task ToStreamAsync_PropagatesFormatToExecutor()
	{
		var executor = new CapturingQueryExecutor();

		using var _ = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Arrow);

		executor.Calls.Should().HaveCount(1);
		executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.ExecuteQueryAsync));
		executor.Calls[0].Format.Should().Be(EsqlFormat.Arrow);
	}

	[Test]
	public void ToStream_GeneratesSameEsqlAsTypedPath()
	{
		var executor = new CapturingQueryExecutor();

		using var _ = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.Take(10)
			.AsEsqlQueryable()
			.ToStream(EsqlFormat.Csv);

		executor.Calls[0].Esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level == "ERROR"
			| LIMIT 10
			""".NativeLineEndings());
	}

	[Test]
	public async Task ToAsyncQueryAsync_PropagatesFormat()
	{
		var executor = new CapturingQueryExecutor();

		await using var _ = await CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Tsv);

		executor.Calls.Should().HaveCount(1);
		executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.SubmitAsyncQueryAsync));
		executor.Calls[0].Format.Should().Be(EsqlFormat.Tsv);
	}

	[Test]
	public void ToAsyncQuery_PropagatesFormat()
	{
		var executor = new CapturingQueryExecutor();

		using var _ = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQuery(EsqlFormat.Json);

		executor.Calls.Should().HaveCount(1);
		executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.SubmitAsyncQuery));
		executor.Calls[0].Format.Should().Be(EsqlFormat.Json);
	}

	[Test]
	public void TypedPath_DoesNotSetFormat()
	{
		var executor = new CapturingQueryExecutor();

		_ = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToList();

		executor.Calls.Should().HaveCount(1);
		executor.Calls[0].Format.Should().BeNull();
	}
}
