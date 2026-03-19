// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderAsyncTests
{
	[Test]
	public async Task ReadRowsAsync_Stream_ReturnsRows()
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
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();
		var rows = new List<ScalarStringModel>();

		await foreach (var row in (await reader.ReadRowsAsync<ScalarStringModel>(stream)).Rows)
			rows.Add(row);

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
	}

	[Test]
	public async Task ReadScalarAsync_Stream_ReturnsFirstValueAndRowCount()
	{
		var json = """
			{
			  "columns": [
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    [10],
			    [20],
			    [30]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		var scalar = await reader.ReadScalarAsync<int>(stream);

		scalar.Value.Should().Be(10);
		scalar.RowCount.Should().Be(3);
	}

	[Test]
	public async Task ReadRowsAsync_Stream_MetadataBeforeValues_CapturedImmediately()
	{
		var json = """
			{
			  "id": "query-123",
			  "is_running": false,
			  "columns": [
			    { "name": "value", "type": "keyword" }
			  ],
			  "values": [
			    ["row-1"]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		var response = await reader.ReadRowsAsync<string>(stream);

		// Metadata is eagerly populated before returning
		response.Id.Should().Be("query-123");
		response.IsRunning.Should().Be(false);

		var rows = new List<string>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().Equal(["row-1"]);
	}

	[Test]
	public async Task ReadRowsAsync_Stream_MetadataAfterValues_InfersIsRunningFalse()
	{
		// Metadata after values is not captured in streaming path.
		// Inference: columns/values present → is_running = false.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ],
			  "id": "query-456",
			  "is_running": true
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		var response = await reader.ReadRowsAsync<int>(stream);

		await foreach (var _ in response.Rows)
		{
		}

		// id after values is not captured; is_running inferred false from columns/values presence
		response.Id.Should().BeNull();
		response.IsRunning.Should().Be(false);
	}

	[Test]
	public async Task ReadRowsAsync_WithCanceledToken_ThrowsOperationCanceledException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" }
			  ],
			  "values": [
			    ["first"]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();
		var cancellationToken = new CancellationToken(canceled: true);

		var act = async () =>
		{
			await foreach (var _ in (await reader.ReadRowsAsync<string>(stream, cancellationToken: cancellationToken)).Rows)
			{
			}
		};

		_ = await act.Should().ThrowAsync<OperationCanceledException>();
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
