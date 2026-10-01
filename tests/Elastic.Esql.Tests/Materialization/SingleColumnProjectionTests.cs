// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;
using Elastic.Esql.Tests.Execution;

namespace Elastic.Esql.Tests.Materialization;

public class SingleColumnProjectionTests
{
	private const string DateJson = """{"columns":[{"name":"timestamp","type":"date"}],"values":[["2024-01-02T03:04:05.000Z"]]}""";
	private const string NullDateJson = """{"columns":[{"name":"timestamp","type":"date"}],"values":[[null]]}""";
	private const string GuidJson = """{"columns":[{"name":"id","type":"keyword"}],"values":[["0f8fad5b-d9cb-469f-a165-70867728950e"]]}""";
	private const string ListJson = """{"columns":[{"name":"tags","type":"keyword"}],"values":[[["a","b"]]]}""";
	private const string MixedListJson = """{"columns":[{"name":"tags","type":"keyword"}],"values":[["only"],[["a","b"]],[null]]}""";
	private const string NameJson = """{"columns":[{"name":"name","type":"keyword"}],"values":[["a"]]}""";

	[Test]
	public void ReadRows_SingleDateColumn_BindsDateTime()
	{
		var rows = ReadRows<DateTime>(DateJson);

		rows.Should().ContainSingle().Which.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
	}

	[Test]
	public void ReadRows_SingleDateColumn_BindsDateTimeOffset()
	{
		var rows = ReadRows<DateTimeOffset>(DateJson);

		rows.Should().ContainSingle().Which.Should().Be(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
	}

	[Test]
	public void ReadRows_SingleKeywordColumn_BindsGuid()
	{
		var rows = ReadRows<Guid>(GuidJson);

		rows.Should().ContainSingle().Which.Should().Be(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
	}

	[Test]
	public void ReadRows_SingleMultiValueColumn_BindsList()
	{
		var rows = ReadRows<List<string>>(ListJson);

		rows.Should().ContainSingle().Which.Should().Equal("a", "b");
	}

	[Test]
	public void ReadRows_SingleValuedMultiValueColumn_WrapsIntoList()
	{
		var rows = ReadRows<List<string>>(MixedListJson);

		rows.Should().HaveCount(3);
		rows[0].Should().Equal("only");
		rows[1].Should().Equal("a", "b");
		rows[2].Should().BeNull();
	}

	[Test]
	public async Task ReadRowsAsync_SingleValuedMultiValueColumn_WrapsIntoList()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MixedListJson));
		await using var results = await CreateReader().ReadRowsAsync<List<string>>(stream);

		var rows = new List<List<string>?>();
		await foreach (var row in results.Rows)
			rows.Add(row);

		rows.Should().HaveCount(3);
		rows[0].Should().Equal("only");
		rows[1].Should().Equal("a", "b");
		rows[2].Should().BeNull();
	}

	[Test]
	public void ReadRows_NullCellForNullableDate_YieldsNull()
	{
		var rows = ReadRows<DateTime?>(NullDateJson);

		rows.Should().ContainSingle().Which.Should().BeNull();
	}

	[Test]
	public void ReadRows_SingleColumnDictionaryTarget_StillAssemblesRow()
	{
		var rows = ReadRows<Dictionary<string, string>>(NameJson);

		rows.Should().ContainSingle().Which.Should().ContainKey("name").WhoseValue.Should().Be("a");
	}

	[Test]
	public void ReadScalar_SingleDateColumn_BindsDateTime()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(DateJson));

		var scalar = CreateReader().ReadScalar<DateTime>(stream);

		scalar.RowCount.Should().Be(1);
		scalar.Value.Year.Should().Be(2024);
	}

	[Test]
	public async Task ReadScalarAsync_SingleDateColumn_BindsDateTime()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(DateJson));

		var scalar = await CreateReader().ReadScalarAsync<DateTime>(stream);

		scalar.RowCount.Should().Be(1);
		scalar.Value.Year.Should().Be(2024);
	}

	[Test]
	public void Select_DateTimeMember_ToList_Materializes()
	{
		var executor = new CapturingQueryExecutor { ResponseJson = DateJson };
		var provider = new EsqlQueryProvider(CreateOptions(), executor);

		var rows = new EsqlQueryable<LogEntry>(provider).From("logs").Select(l => l.Timestamp).ToList();

		rows.Should().ContainSingle().Which.Year.Should().Be(2024);
	}

	[Test]
	public async Task Select_DateTimeMember_FirstAsync_Materializes()
	{
		var executor = new CapturingQueryExecutor { ResponseJson = DateJson };
		var provider = new EsqlQueryProvider(CreateOptions(), executor);

		var value = await new EsqlQueryable<LogEntry>(provider).From("logs").Select(l => l.Timestamp).AsEsqlQueryable().FirstAsync();

		value.Year.Should().Be(2024);
	}

	private static List<T> ReadRows<T>(string json)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = CreateReader().ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	// Mirrors EsqlClientSettings.ResolveJsonOptions: the source-generated contexts first, reflection behind them.
	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default,
				new DefaultJsonTypeInfoResolver()
			)
		};

	private static EsqlResponseReader CreateReader() =>
		new(new JsonMetadataManager(CreateOptions()));
}
