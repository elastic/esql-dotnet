// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Tests.Translation;

public class ParameterizedDoubleTests : EsqlTestBase
{
	[Test]
	public void Where_CapturedWholeDouble_ParameterKeepsDecimalPoint()
	{
		var threshold = 100.0;

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration / threshold > 1);

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["threshold"].GetRawText().Should().Be("100.0");
	}

	[Test]
	public void Where_CapturedFractionalDouble_ParameterUnchanged()
	{
		var threshold = 99.5;

		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration / threshold > 1);

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["threshold"].GetRawText().Should().Be("99.5");
	}

	[Test]
	public void Row_CapturedWholeDoubleArray_ParameterKeepsDecimalPoints()
	{
		var values = new[] { 100.0, 200.5 };

		var query = CreateQuery<LogEntry>()
			.Row(() => new { vals = values });

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["vals"].GetRawText().Should().Be("[100.0,200.5]");
	}

	[Test]
	public void Row_CapturedFloatList_ParameterKeepsDecimalPoints()
	{
		var values = new List<float> { 1f, 2.5f };

		var query = CreateQuery<LogEntry>()
			.Row(() => new { vals = values });

		_ = query.ToEsqlString(inlineParameters: false);
		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["vals"].GetRawText().Should().Be("[1.0,2.5]");
	}

	[Test]
	public void GetParameters_DoubleWithRegisteredConverter_UsesConverterOutput()
	{
		var provider = new EsqlQueryProvider(new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new PrefixedDoubleConverter() }
		});
		var threshold = 100.0;

		var query = new EsqlQueryable<LogEntry>(provider)
			.From("logs-*")
			.Where(l => l.Duration > threshold);

		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["threshold"].GetString().Should().Be("D:100");
	}

	[Test]
	public void GetParameters_DoubleArrayWithRegisteredConverter_UsesConverterOutput()
	{
		// The array itself is serialized through the options, so its metadata must be resolvable as well.
		var provider = new EsqlQueryProvider(new JsonSerializerOptions
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(EsqlTestMappingContext.Default, new DefaultJsonTypeInfoResolver()),
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new PrefixedDoubleConverter() }
		});
		var values = new[] { 100.0, 200.5 };

		var query = new EsqlQueryable<LogEntry>(provider)
			.Row(() => new { vals = values });

		var parameters = query.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters.Parameters["vals"].GetRawText().Should().Be("""["D:100","D:200.5"]""");
	}

	private sealed class PrefixedDoubleConverter : JsonConverter<double>
	{
		public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var text = reader.GetString() ?? throw new JsonException("Expected a string.");
			return double.Parse(text.AsSpan(2), CultureInfo.InvariantCulture);
		}

		public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
			writer.WriteStringValue($"D:{value.ToString(CultureInfo.InvariantCulture)}");
	}

	[Test]
	public void GetParameters_CapturedPositiveInfinity_ThrowsNotSupported()
	{
		var max = double.PositiveInfinity;
		var query = CreateQuery<LogEntry>().From("logs-*").Where(l => l.Duration < max);

		var act = () => query.GetParameters();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void GetParameters_CapturedDoubleArrayWithNaN_ThrowsNotSupported()
	{
		var values = new[] { 1.0, double.NaN };
		var query = CreateQuery<LogEntry>().From("logs-*").Where(l => values.Contains(l.Duration));

		var act = () => query.GetParameters();

		_ = act.Should().Throw<NotSupportedException>();
	}
}
