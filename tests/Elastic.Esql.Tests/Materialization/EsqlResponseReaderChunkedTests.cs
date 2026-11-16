// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderChunkedTests
{
	private const string RowsJson =
		"""{"columns":[{"name":"value","type":"keyword"},{"name":"count","type":"integer"}],"values":[["first",1],["second",2]]}""";

	private const string RowsWithTrailingIdJson =
		"""{"columns":[{"name":"value","type":"keyword"},{"name":"count","type":"integer"}],"values":[["first",1],["second",2]],"id":"query-456"}""";

	[Test]
	public void ReadRows_Stream_HeaderSpansMultipleChunks_ReturnsRows()
	{
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(RowsJson), maxBytesPerRead: 3);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
	}

	[Test]
	public async Task ReadRowsAsync_Stream_HeaderSpansMultipleChunks_ReturnsRows()
	{
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(RowsJson), maxBytesPerRead: 3);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
	}

	[Test]
	public void ReadRows_Stream_RequireId_IdScanSpansMultipleChunks_CapturesId()
	{
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(RowsWithTrailingIdJson), maxBytesPerRead: 3);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream, requireId: true);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		response.Id.Should().Be("query-456");
	}

	[Test]
	public async Task ReadRowsAsync_Stream_RequireId_IdScanSpansMultipleChunks_CapturesId()
	{
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(RowsWithTrailingIdJson), maxBytesPerRead: 3);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream, requireId: true);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		response.Id.Should().Be("query-456");
	}

	[Test]
	public void ReadRows_Stream_HeaderWithMoreColumnsThanTheFirstRent_KeepsEveryColumn()
	{
		const string json =
			"""{"columns":[{"name":"c00","type":"integer"},{"name":"c01","type":"integer"},{"name":"c02","type":"integer"},""" +
			"""{"name":"c03","type":"integer"},{"name":"c04","type":"integer"},{"name":"c05","type":"integer"},""" +
			"""{"name":"c06","type":"integer"},{"name":"c07","type":"integer"},{"name":"c08","type":"integer"},""" +
			"""{"name":"c09","type":"integer"},{"name":"c10","type":"integer"},{"name":"c11","type":"integer"},""" +
			"""{"name":"c12","type":"integer"},{"name":"c13","type":"integer"},{"name":"c14","type":"integer"},""" +
			"""{"name":"c15","type":"integer"},{"name":"c16","type":"integer"}],"values":[[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16]]}""";
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(json), maxBytesPerRead: 3);
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
		using var response = new EsqlResponseReader(new JsonMetadataManager(options)).ReadRows<Dictionary<string, int>>(stream);

		var row = response.Rows.Single();

		row.Should().HaveCount(17);
		row["c00"].Should().Be(0);
		row["c16"].Should().Be(16);
	}

	private static EsqlResponseReader CreateReader()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};

		var metadata = new JsonMetadataManager(options);
		return new EsqlResponseReader(metadata);
	}
}
