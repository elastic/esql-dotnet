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
