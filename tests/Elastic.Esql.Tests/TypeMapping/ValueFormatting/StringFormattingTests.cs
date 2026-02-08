// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Formatting;

namespace Elastic.Esql.Tests.TypeMapping.ValueFormatting;

public class StringFormattingTests
{
	[Test]
	public void FormatValue_String_ReturnsQuoted()
	{
		var result = EsqlFormatting.FormatValue("hello");

		_ = result.Should().Be("\"hello\"");
	}

	[Test]
	public void FormatValue_EmptyString_ReturnsEmptyQuoted()
	{
		var result = EsqlFormatting.FormatValue("");

		_ = result.Should().Be("\"\"");
	}

	[Test]
	public void FormatValue_StringWithQuotes_EscapesQuotes()
	{
		var result = EsqlFormatting.FormatValue("say \"hello\"");

		_ = result.Should().Be("\"say \\\"hello\\\"\"");
	}

	[Test]
	public void FormatValue_StringWithBackslash_EscapesBackslash()
	{
		var result = EsqlFormatting.FormatValue("path\\to\\file");

		_ = result.Should().Be("\"path\\\\to\\\\file\"");
	}

	[Test]
	public void FormatValue_StringWithNewline_EscapesNewline()
	{
		var result = EsqlFormatting.FormatValue("line1\nline2");

		_ = result.Should().Be("\"line1\\nline2\"");
	}

	[Test]
	public void FormatValue_StringWithTab_EscapesTab()
	{
		var result = EsqlFormatting.FormatValue("col1\tcol2");

		_ = result.Should().Be("\"col1\\tcol2\"");
	}
}
