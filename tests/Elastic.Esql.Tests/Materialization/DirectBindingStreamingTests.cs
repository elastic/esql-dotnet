// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectBindingStreamingTests
{
	private const string FlatJson = """
		{
		  "columns": [
		    { "name": "value", "type": "keyword" },
		    { "name": "count", "type": "integer" }
		  ],
		  "values": [
		    ["first", 1],
		    ["second", 2],
		    ["third", 3]
		  ]
		}
		""";

	[Test]
	public async Task ReadRowsAsync_EligibleFlatModel_ReturnsAllRows()
	{
		using var stream = CreateStream(FlatJson);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(3);
		rows[0].Value.Should().Be("first");
		rows[0].Count.Should().Be(1);
		rows[2].Value.Should().Be("third");
		rows[2].Count.Should().Be(3);
	}

	[Test]
	public void ReadRows_OneByteChunkedStream_ReturnsAllRows()
	{
		// Single-byte reads force TryReadNextRow to hit the incomplete-row restore path on
		// nearly every call, proving the fast path never commits partial reader state.
		using var stream = new OneByteStream(Encoding.UTF8.GetBytes(FlatJson));
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(3);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
		rows[2].Value.Should().Be("third");
		rows[2].Count.Should().Be(3);
	}

	[Test]
	public async Task ReadRowsAsync_OneByteChunkedStream_ReturnsAllRows()
	{
		using var stream = new OneByteStream(Encoding.UTF8.GetBytes(FlatJson));
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		rows.Should().HaveCount(3);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
		rows[2].Value.Should().Be("third");
	}

	[Test]
	public void ReadScalar_FlatModelMultiColumn_ReturnsFirstRowAndCount()
	{
		using var stream = CreateStream(FlatJson);
		var reader = CreateReader();

		var scalar = reader.ReadScalar<ScalarStringModel>(stream);

		scalar.Value.Should().NotBeNull();
		scalar.Value!.Value.Should().Be("first");
		scalar.Value.Count.Should().Be(1);
		scalar.RowCount.Should().Be(3);
	}

	[Test]
	public async Task ReadScalarAsync_FlatModelMultiColumn_ReturnsFirstRowAndCount()
	{
		using var stream = CreateStream(FlatJson);
		var reader = CreateReader();

		var scalar = await reader.ReadScalarAsync<ScalarStringModel>(stream);

		scalar.Value.Should().NotBeNull();
		scalar.Value!.Value.Should().Be("first");
		scalar.Value.Count.Should().Be(1);
		scalar.RowCount.Should().Be(3);
	}

	[Test]
	public void ReadRows_ValuesBeforeColumns_BufferedPathStillMaterializes()
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
			  ]
			}
			""";

		using var stream = CreateStream(json);
		var reader = CreateReader();

		using var response = reader.ReadRows<ScalarStringModel>(stream);
		var rows = response.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Value.Should().Be("first");
		rows[1].Value.Should().Be("second");
		rows[1].Count.Should().Be(2);
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
