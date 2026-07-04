// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectBindingFallbackTests
{
	[Test]
	public void ReadRows_MixedCoercionRows_AllRowsCorrect()
	{
		// Row 2 carries a string-encoded number; the eligible layout binds rows 1 and 3 directly
		// while row 2 falls back to the serializer, which honors AllowReadingFromString.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 1],
			    ["b", "42"],
			    ["c", 3]
			  ]
			}
			""";

		var results = ReadRows<ScalarStringModel>(json);

		results.Should().HaveCount(3);
		results[0].Value.Should().Be("a");
		results[0].Count.Should().Be(1);
		results[1].Value.Should().Be("b");
		results[1].Count.Should().Be(42);
		results[2].Value.Should().Be("c");
		results[2].Count.Should().Be(3);
	}

	[Test]
	public async Task ReadRowsAsync_MixedCoercionRows_AllRowsCorrect()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 1],
			    ["b", "42"],
			    ["c", 3]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(3);
		rows[1].Count.Should().Be(42);
	}

	[Test]
	public void ReadRows_MultiValueCellMidStream_YieldsPriorRowsThenThrows()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 1],
			    [["x", "y"], 2]
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();
		using var response = reader.ReadRows<ScalarStringModel>(stream);
		using var enumerator = response.Rows.GetEnumerator();

		enumerator.MoveNext().Should().BeTrue();
		enumerator.Current.Value.Should().Be("a");
		enumerator.Current.Count.Should().Be(1);

		var act = () => enumerator.MoveNext();

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_IntOverflowCell_FallsBackAndThrowsJsonException()
	{
		// 99999999999 exceeds Int32; TryGetInt32 rejects it, the row falls back, and the
		// serializer raises its canonical JsonException.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 99999999999]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarStringModel>(json);

		_ = act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_RowWithFewerCells_ThrowsFewerValuesJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a"]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarStringModel>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*fewer values*");
	}

	[Test]
	public void ReadRows_RowWithMoreCells_ThrowsMoreValuesJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 1, 2]
			  ]
			}
			""";

		var act = () => ReadRows<ScalarStringModel>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*more values*");
	}

	[Test]
	public void ReadRows_OneByteChunkedStreamWithFallbackRow_AllRowsCorrect()
	{
		// Combines the incomplete-row restore path with the per-row fallback path.
		var json = """
			{
			  "columns": [
			    { "name": "value", "type": "keyword" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["a", 1],
			    ["b", "42"],
			    ["c", 3]
			  ]
			}
			""";

		using var stream = new OneByteStream(Encoding.UTF8.GetBytes(json));
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(3);
		rows[0].Count.Should().Be(1);
		rows[1].Count.Should().Be(42);
		rows[2].Count.Should().Be(3);
	}

	private static List<T> ReadRows<T>(string json)
	{
		using var stream = CreateStream(json);
		var reader = CreateReader();
		using var results = reader.ReadRows<T>(stream);
		return results.Rows.ToList();
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

	private sealed class OneByteStream(byte[] data) : Stream
	{
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => data.Length;
		public override long Position { get => _position; set => throw new NotSupportedException(); }

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_position >= data.Length)
				return 0;

			buffer[offset] = data[_position];
			_position++;
			return 1;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
