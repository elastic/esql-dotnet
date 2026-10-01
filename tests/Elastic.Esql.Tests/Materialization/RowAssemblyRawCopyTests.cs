// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class RowAssemblyRawCopyTests
{
	private const string Columns =
		"""[{"name":"text","type":"keyword"},{"name":"count","type":"integer"},""" +
		"""{"name":"attributes","type":"object"},{"name":"numbers","type":"integer"}]""";

	[Test]
	public void ReadRows_EscapedStringAndNestedCells_RoundTrip()
	{
		var json = $$$"""{"columns":{{{Columns}}},"values":[["a\"b\\cé\n",1,{"k\"q":"v\\w"},[1,2,3]]]}""";

		var rows = ReadRows<EscapedCellsRecord>(json);

		rows.Should().ContainSingle();
		rows[0].Text.Should().Be("a\"b\\cé\n");
		rows[0].Count.Should().Be(1);
		rows[0].Attributes.Should().ContainKey("k\"q").WhoseValue.Should().Be("v\\w");
		rows[0].Numbers.Should().Equal(1, 2, 3);
	}

	[Test]
	public void ReadRows_SingleValueForCollectionColumn_IsWrapped()
	{
		var json = $$$"""{"columns":{{{Columns}}},"values":[["t",2,null,5]]}""";

		var rows = ReadRows<EscapedCellsRecord>(json);

		rows.Should().ContainSingle();
		rows[0].Attributes.Should().BeNull();
		rows[0].Numbers.Should().Equal(5);
	}

	[Test]
	public void ReadRows_ComplexCellSplitAcrossReads_RoundTrips()
	{
		var json = $$$"""{"columns":{{{Columns}}},"values":[["x",3,{"a":"b","c":"d"},[10,20,30]],["y",4,null,null]]}""";
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(json), maxBytesPerRead: 7);
		using var results = CreateReader().ReadRows<EscapedCellsRecord>(stream);

		var rows = results.Rows.ToList();

		rows.Should().HaveCount(2);
		rows[0].Attributes.Should().HaveCount(2);
		rows[0].Numbers.Should().Equal(10, 20, 30);
		rows[1].Text.Should().Be("y");
		rows[1].Numbers.Should().BeNull();
	}

	[Test]
	public void ReadRows_FlatRowWithNullCells_SkipsThem()
	{
		const string json =
			"""{"columns":[{"name":"first","type":"keyword"},{"name":"middle","type":"integer"},""" +
			"""{"name":"last","type":"keyword"}],"values":[[null,1,"z"],["a",null,null]]}""";

		var rows = ReadRows<NullableCellsRecord>(json);

		rows.Should().HaveCount(2);
		rows[0].Should().Be(new NullableCellsRecord(null, 1, "z"));
		rows[1].Should().Be(new NullableCellsRecord("a", null, null));
	}

	[Test]
	public void ReadRows_MoreCellsThanColumns_Throws()
	{
		const string json = """{"columns":[{"name":"first","type":"keyword"}],"values":[["a","b"]]}""";

		var act = () => ReadRows<NullableCellsRecord>(json);

		act.Should().Throw<JsonException>().WithMessage("*more values than declared columns*");
	}

#if NET10_0_OR_GREATER
	[Test]
	public async Task ReadRowsAsync_StringCellSpanningPipeSegments_RoundTrips()
	{
		var longText = new string('x', 9000) + "\"tail\"";
		var json = $$$"""{"columns":{{{Columns}}},"values":[[{{{JsonSerializer.Serialize(longText)}}},1,null,null]]}""";
		var pipe = new Pipe();
		await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(json));
		await pipe.Writer.CompleteAsync();

		await using var results = await CreateReader().ReadRowsAsync<EscapedCellsRecord>(pipe.Reader);
		var rows = new List<EscapedCellsRecord>();
		await foreach (var row in results.Rows)
			rows.Add(row);

		rows.Should().ContainSingle().Which.Text.Should().Be(longText);
	}

	[Test]
	public async Task ReadRowsAsync_ComplexCellsSpanningPipeSegments_RoundTrip()
	{
		var attributes = Enumerable.Range(0, 300).ToDictionary(i => $"key{i:D3}", i => $"value{i:D3}".PadRight(20, 'x'));
		var numbers = Enumerable.Range(0, 3000).ToList();
		var json = $$$"""
			{"columns":{{{Columns}}},"values":[["t",1,{{{JsonSerializer.Serialize(attributes)}}},{{{JsonSerializer.Serialize(numbers)}}}]]}
			""";
		var pipe = new Pipe();
		await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(json));
		await pipe.Writer.CompleteAsync();

		await using var results = await CreateReader().ReadRowsAsync<EscapedCellsRecord>(pipe.Reader);
		var rows = new List<EscapedCellsRecord>();
		await foreach (var row in results.Rows)
			rows.Add(row);

		var single = rows.Should().ContainSingle().Subject;
		single.Attributes.Should().BeEquivalentTo(attributes);
		single.Numbers.Should().Equal(numbers);
	}
#endif

	private static List<T> ReadRows<T>(string json)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = CreateReader().ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static EsqlResponseReader CreateReader() =>
		new(new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(MaterializationTestJsonContext.Default, EsqlTestMappingContext.Default)
		}));
}
