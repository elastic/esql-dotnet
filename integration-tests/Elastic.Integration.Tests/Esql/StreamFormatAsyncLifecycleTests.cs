// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Esql;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Integration.Tests.Esql;

public class StreamFormatAsyncLifecycleTests : IntegrationTestBase
{
	[Test]
	public async Task ToAsyncQueryAsync_KeepOnCompletion_HasQueryIdFromHeader()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Csv, options);

		q.QueryId.Should().NotBeNullOrWhiteSpace("X-Elasticsearch-Async-Id must be parsed from response headers");
	}

	[Test]
	public async Task WaitForCompletionAsync_RawCsv_ReturnsCompletedStream()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Csv, options);

		await q.WaitForCompletionAsync(TimeSpan.FromMilliseconds(100));

		q.IsCompleted.Should().BeTrue();

		using var stream = q.GetResponseStream();
		var text = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain(",", "CSV body must contain delimiters once the query completes");
	}

	[Test]
	public async Task WaitForCompletionAsync_RawArrow_ReturnsArrowStream()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Arrow, options);

		await q.WaitForCompletionAsync(TimeSpan.FromMilliseconds(100));

		q.IsCompleted.Should().BeTrue();

		using var stream = q.GetResponseStream();
		using var ms = new MemoryStream();
		await stream.CopyToAsync(ms);
		var bytes = ms.ToArray();

		bytes.Length.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task GetResponseStream_BeforeCompletion_Throws()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Csv, options);

		// With WaitForCompletionTimeout=0 the server immediately returns is_running=true
		if (q.IsRunning)
		{
			var act = () => q.GetResponseStream();
			act.Should().Throw<InvalidOperationException>();
		}
	}

	[Test]
	public async Task RefreshAsync_UpdatesState()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Csv, options);

		q.QueryId.Should().NotBeNullOrWhiteSpace();

		while (!q.IsCompleted)
			await q.RefreshAsync();

		q.IsCompleted.Should().BeTrue();
	}

	[Test]
	public async Task SyncSubmit_RawCsv_LifecycleWorks()
	{
		var options = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.Zero,
			KeepOnCompletion = true
		};

		using var q = Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToAsyncQuery(EsqlFormat.Csv, options);

		q.QueryId.Should().NotBeNullOrWhiteSpace();

		q.WaitForCompletion(TimeSpan.FromMilliseconds(100));

		q.IsCompleted.Should().BeTrue();

		using var stream = q.GetResponseStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		var text = reader.ReadToEnd();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain(",");
	}

	[Test]
	public async Task FastQuery_NoTimeout_ImmediateCompletion()
	{
		// Fast query without zero timeout: completes within the server's wait window.
		await using var q = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(1)
			.AsEsqlQueryable()
			.ToAsyncQueryAsync(EsqlFormat.Csv);

		q.IsCompleted.Should().BeTrue();

		using var stream = q.GetResponseStream();
		var text = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();
		text.Should().NotBeNullOrWhiteSpace();
	}
}
