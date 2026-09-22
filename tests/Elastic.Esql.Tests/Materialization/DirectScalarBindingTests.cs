// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class DirectScalarBindingTests
{
	private const string IntsJson = """{"columns":[{"name":"n","type":"integer"}],"values":[[1],[2],[3]]}""";
	private const string IntWithNullJson = """{"columns":[{"name":"n","type":"integer"}],"values":[[1],[null]]}""";
	private const string FractionJson = """{"columns":[{"name":"n","type":"double"}],"values":[[1.5]]}""";
	private const string QuotedIntJson = """{"columns":[{"name":"n","type":"long"}],"values":[["12"]]}""";
	private const string EscapedStringJson = """{"columns":[{"name":"s","type":"keyword"}],"values":[["a\"b\\c"]]}""";
	private const string EpochJson = """{"columns":[{"name":"d","type":"long"}],"values":[[86400]]}""";

	[Test]
	public void ReadRows_IntColumn_BindsEveryRow() =>
		ReadRows<int>(IntsJson).Should().Equal(1, 2, 3);

	[Test]
	public void ReadRows_NullableIntWithNullCell_YieldsNull() =>
		ReadRows<int?>(IntWithNullJson).Should().Equal(1, null);

	[Test]
	public void ReadRows_IntWithNullCell_Throws()
	{
		var act = () => ReadRows<int>(IntWithNullJson);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_DoubleFromIntegerToken_Binds() =>
		ReadRows<double>(IntsJson).Should().Equal(1d, 2d, 3d);

	[Test]
	public void ReadRows_IntFromFraction_Throws()
	{
		var act = () => ReadRows<int>(FractionJson);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_LongFromQuotedNumber_ThrowsUnderStrictNumberHandling()
	{
		var options = CreateOptions();
		options.NumberHandling = JsonNumberHandling.Strict;

		var act = () => ReadRows<long>(QuotedIntJson, options);

		act.Should().Throw<JsonException>();
	}

	[Test]
	public void ReadRows_LongFromQuotedNumber_HonorsAllowReadingFromString()
	{
		var options = CreateOptions();
		options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

		ReadRows<long>(QuotedIntJson, options).Should().Equal(12L);
	}

	[Test]
	public void ReadRows_EscapedString_Unescapes() =>
		ReadRows<string>(EscapedStringJson).Should().Equal("a\"b\\c");

	[Test]
	public void ReadRows_DateTimeWithRegisteredConverter_UsesConverter()
	{
		var options = CreateOptions();
		options.Converters.Add(new UnixEpochDateTimeConverter());

		ReadRows<DateTime>(EpochJson, options).Should().Equal(DateTime.UnixEpoch.AddDays(1));
	}

	[Test]
	public void ReadRows_OneByteReads_BindsEveryRow()
	{
		using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(IntsJson), maxBytesPerRead: 1);
		using var results = CreateReader(CreateOptions()).ReadRows<int>(stream);

		results.Rows.ToList().Should().Equal(1, 2, 3);
	}

	[Test]
	public void ReadScalar_IntColumn_ReturnsFirstValueAndRowCount()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(IntsJson));

		var scalar = CreateReader(CreateOptions()).ReadScalar<int>(stream);

		scalar.Value.Should().Be(1);
		scalar.RowCount.Should().Be(3);
	}

	[Test]
	public async Task ReadScalarAsync_StringColumn_ReturnsFirstValue()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(EscapedStringJson));

		var scalar = await CreateReader(CreateOptions()).ReadScalarAsync<string>(stream);

		scalar.Value.Should().Be("a\"b\\c");
		scalar.RowCount.Should().Be(1);
	}

	private static List<T> ReadRows<T>(string json, JsonSerializerOptions? options = null)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var results = CreateReader(options ?? CreateOptions()).ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	private static EsqlResponseReader CreateReader(JsonSerializerOptions options) =>
		new(new JsonMetadataManager(options));

	private static JsonSerializerOptions CreateOptions() =>
		new(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default,
				new DefaultJsonTypeInfoResolver()
			)
		};

	private sealed class UnixEpochDateTimeConverter : JsonConverter<DateTime>
	{
		public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			DateTime.UnixEpoch.AddSeconds(reader.GetInt64());

		public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
			writer.WriteNumberValue((long)(value - DateTime.UnixEpoch).TotalSeconds);
	}
}
