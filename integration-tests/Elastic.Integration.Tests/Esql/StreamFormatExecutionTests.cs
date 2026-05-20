// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using Elastic.Esql;

namespace Elastic.Esql.Integration.Tests.Esql;

public class StreamFormatExecutionTests : IntegrationTestBase
{
	[Test]
	public async Task ToStreamAsync_Csv_ReturnsCsvBody()
	{
		using var stream = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Csv);

		var text = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain(",", "CSV must contain comma delimiters");

		var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		lines.Length.Should().BeGreaterThan(1, "CSV should have at least a header and one row");
	}

	[Test]
	public async Task ToStreamAsync_Tsv_ReturnsTsvBody()
	{
		using var stream = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Tsv);

		var text = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain("\t", "TSV must contain tab delimiters");
	}

	[Test]
	public async Task ToStreamAsync_Txt_ReturnsHumanReadableTable()
	{
		using var stream = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Txt);

		var text = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain("|", "TXT output uses pipe separators");
	}

	[Test]
	public async Task ToStreamAsync_Json_ReturnsParseableJsonEnvelope()
	{
		using var stream = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(2)
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Json);

		using var doc = await JsonDocument.ParseAsync(stream);

		doc.RootElement.TryGetProperty("columns", out _).Should().BeTrue();
		doc.RootElement.TryGetProperty("values", out var values).Should().BeTrue();
		values.GetArrayLength().Should().BeGreaterThan(0);
	}

	[Test]
	public async Task ToStreamAsync_Arrow_ReturnsArrowMagicBytes()
	{
		using var stream = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(5)
			.AsEsqlQueryable()
			.ToStreamAsync(EsqlFormat.Arrow);

		using var ms = new MemoryStream();
		await stream.CopyToAsync(ms);
		var bytes = ms.ToArray();

		bytes.Length.Should().BeGreaterThan(0);
		// Arrow IPC stream begins with the continuation indicator 0xFFFFFFFF followed by metadata length.
		// Some implementations use the legacy header — either way the first 4 bytes are non-zero.
		(bytes[0] | bytes[1] | bytes[2] | bytes[3]).Should().NotBe(0);
	}

	[Test]
	public void ToStream_Csv_SyncOverload()
	{
		using var stream = Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToStream(EsqlFormat.Csv);

		using var reader = new StreamReader(stream, Encoding.UTF8);
		var text = reader.ReadToEnd();

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain(",");
	}

	[Test]
	public async Task ToPipeReaderAsync_Csv_ReturnsPipeWithCsvBytes()
	{
		var pipe = await Fixture.EsqlClient
			.CreateQuery<TestProduct>()
			.From(TestDataSeeder.ProductIndex)
			.Take(3)
			.AsEsqlQueryable()
			.ToPipeReaderAsync(EsqlFormat.Csv);

		var ms = new MemoryStream();
		await pipe.CopyToAsync(ms);
		pipe.Complete();

		var text = Encoding.UTF8.GetString(ms.ToArray());

		text.Should().NotBeNullOrWhiteSpace();
		text.Should().Contain(",");
	}
}
