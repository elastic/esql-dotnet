// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderBufferedTests
{
	[Test]
	public void ReadRows_Stream_ValuesPropertyBeforeColumns_UsesBufferedFallback()
	{
		var json = """
			{
			  "values": [
			    ["first", 1],
			    ["second", 2]
			  ],
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "id": "query-123",
			  "is_running": false
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
		response.Id.Should().BeNull();
		response.IsRunning.Should().BeNull();
	}

	[Test]
	public async Task ReadRowsAsync_Stream_ValuesPropertyBeforeColumns_UsesBufferedFallback()
	{
		var json = """
			{
			  "values": [
			    ["first", 1],
			    ["second", 2]
			  ],
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "id": "query-123",
			  "is_running": false
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
		response.Id.Should().BeNull();
		response.IsRunning.Should().BeNull();
	}

	[Test]
	public void ReadRows_Stream_RequireId_IdAfterValues_BuffersAndCapturesId()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1],
			    ["second", 2]
			  ],
			  "id": "query-456"
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream, requireId: true);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
		response.Id.Should().Be("query-456");
	}

	[Test]
	public async Task ReadRowsAsync_Stream_RequireId_IdAfterValues_BuffersAndCapturesId()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1],
			    ["second", 2]
			  ],
			  "id": "query-456"
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream, requireId: true);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
		response.Id.Should().Be("query-456");
	}

	[Test]
	public void ReadRows_Stream_RequireId_IdBeforeValues_DoesNotBuffer()
	{
		var json = """
			{
			  "id": "query-789",
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["first", 1],
			    ["second", 2]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream, requireId: true);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
		response.Id.Should().Be("query-789");
		response.IsRunning.Should().Be(false);
	}

	[Test]
	public void ReadRows_Stream_ValuesFirst_WithMetadata_CapturesColumnsAndRows()
	{
		var json = """
			{
			  "id": "query-meta",
			  "is_running": false,
			  "values": [
			    ["alpha", 10],
			    ["beta", 20]
			  ],
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("alpha");
		rows[0].Count.Should().Be(10);
		rows[1].Value.Should().Be("beta");
		rows[1].Count.Should().Be(20);
		response.Id.Should().Be("query-meta");
		response.IsRunning.Should().Be(false);
	}

	[Test]
	public void ReadRows_Stream_EmptyValues_ReturnsNoRows()
	{
		var json = """
			{
			  "values": [],
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(0);
	}

	[Test]
	public void ReadRows_Stream_LargeResponse_HandlesBufferGrowth()
	{
		var sb = new StringBuilder();
		sb.AppendLine("""{ "values": [""");
		for (var i = 0; i < 500; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.AppendLine($"""["item-{i}", {i}]""");
		}
		sb.AppendLine("""], "columns": [ { "name": "value", "type": "keyword" }, { "name": "count", "type": "integer" } ] }""");

		var json = sb.ToString();

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(500);
		rows[0].Value.Should().Be("item-0");
		rows[0].Count.Should().Be(0);
		rows[499].Value.Should().Be("item-499");
		rows[499].Count.Should().Be(499);
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

	private static MemoryStream CreateStream(string json) => new(Encoding.UTF8.GetBytes(json));
}
