// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.Execution;

public class FormatConsistencyTests
{
	private static EsqlQueryable<T> CreateExecutableQuery<T>(CapturingQueryExecutor executor, IEsqlQueryInterceptor? interceptor = null) =>
		new(new EsqlQueryProvider(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			},
			executor
		)
		{ Interceptor = interceptor });

	private sealed class FormatInterceptor(EsqlFormat? format) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query) => query.WithFormat(format);
	}

	[Test]
	public void ToList_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.ToList();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public void Count_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.Count();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task ToListAsync_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToListAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task FirstOrDefaultAsync_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.FirstOrDefaultAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task FirstOrDefaultAsync_InterceptorSetsJson_PassesFormatToExecutor()
	{
		var executor = new CapturingQueryExecutor();

		_ = await CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Json))
			.From("logs-*")
			.AsEsqlQueryable()
			.FirstOrDefaultAsync();

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Json);
	}

	[Test]
	public void ToAsyncQuery_Typed_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQuery();

		_ = act.Should().Throw<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task ToAsyncQueryAsync_Typed_InterceptorSetsCsv_Throws()
	{
		var executor = new CapturingQueryExecutor();

		var act = () => CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQueryAsync();

		_ = await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Csv*");
		_ = executor.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task ToAsyncQueryAsync_Typed_InterceptorSetsJson_PassesFormatToExecutor()
	{
		var executor = new CapturingQueryExecutor();

		await using var asyncQuery = await CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Json))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQueryAsync();

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Method.Should().Be(nameof(IEsqlQueryExecutor.SubmitAsyncQueryAsync));
		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Json);
	}

	[Test]
	public void ToStream_NoArgument_UsesInterceptorFormat()
	{
		var executor = new CapturingQueryExecutor();

		using var stream = CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToStream();

		_ = executor.Calls.Should().HaveCount(1);
		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Csv);
	}

	[Test]
	public void ToStream_ExplicitArgument_OverridesInterceptorFormat()
	{
		var executor = new CapturingQueryExecutor();

		using var stream = CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToStream(EsqlFormat.Arrow);

		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Arrow);
	}

	[Test]
	public void ToStream_NoArgumentNoModelFormat_DefaultsToJson()
	{
		var executor = new CapturingQueryExecutor();

		using var stream = CreateExecutableQuery<LogEntry>(executor)
			.From("logs-*")
			.AsEsqlQueryable()
			.ToStream();

		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Json);
	}

	[Test]
	public async Task ToStreamAsync_NoArgument_UsesInterceptorFormat()
	{
		var executor = new CapturingQueryExecutor();

		using var stream = await CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Tsv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToStreamAsync();

		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Tsv);
	}

	[Test]
	public void ToAsyncQuery_RawFormatArgument_OverridesInterceptorFormat()
	{
		var executor = new CapturingQueryExecutor();

		using var asyncQuery = CreateExecutableQuery<LogEntry>(executor, new FormatInterceptor(EsqlFormat.Csv))
			.From("logs-*")
			.AsEsqlQueryable()
			.ToAsyncQuery(EsqlFormat.Tsv);

		_ = executor.Calls[0].Format.Should().Be(EsqlFormat.Tsv);
	}
}
