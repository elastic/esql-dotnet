// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Formatting;

namespace Elastic.Esql.Tests.TypeMapping.ValueFormatting;

public class DateTimeFormattingTests : EsqlTestBase
{
	[Test]
	public void FormatValue_DateTimeUtc_ReturnsIso8601()
	{
		var dt = new DateTime(2024, 1, 15, 10, 30, 45, 123, DateTimeKind.Utc);
		var result = EsqlFormatting.FormatValue(dt, ReaderOptions);

		_ = result.Should().Be("\"2024-01-15T10:30:45.123Z\"");
	}

	[Test]
	public void FormatValue_DateOnly_ReturnsDateString()
	{
		var d = new DateOnly(2024, 1, 15);
		var result = EsqlFormatting.FormatValue(d, ReaderOptions);

		_ = result.Should().Be("\"2024-01-15\"");
	}

	[Test]
	public void FormatValue_TimeOnly_ReturnsTimeString()
	{
		var t = new TimeOnly(10, 30, 45);
		var result = EsqlFormatting.FormatValue(t, ReaderOptions);

		_ = result.Should().Be("\"10:30:45\"");
	}

	[Test]
	public void FormatValue_Guid_ReturnsQuotedString()
	{
		var guid = new Guid("12345678-1234-1234-1234-123456789012");
		var result = EsqlFormatting.FormatValue(guid, ReaderOptions);

		_ = result.Should().Be("\"12345678-1234-1234-1234-123456789012\"");
	}

	[Test]
	public void FormatValue_Enum_ReturnsOrdinalByDefault()
	{
		var result = EsqlFormatting.FormatValue(LogLevel.Error, ReaderOptions);

		_ = result.Should().Be("3");
	}
}
