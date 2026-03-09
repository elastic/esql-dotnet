// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Formatting;

namespace Elastic.Esql.Tests.TypeMapping.ValueFormatting;

public class NumericFormattingTests : EsqlTestBase
{
	[Test]
	public void FormatValue_Null_ReturnsNull()
	{
		var result = EsqlFormatting.FormatValue(null, ReaderOptions);

		_ = result.Should().Be("null");
	}

	[Test]
	public void FormatValue_True_ReturnsTrueLowercase()
	{
		var result = EsqlFormatting.FormatValue(true, ReaderOptions);

		_ = result.Should().Be("true");
	}

	[Test]
	public void FormatValue_False_ReturnsFalseLowercase()
	{
		var result = EsqlFormatting.FormatValue(false, ReaderOptions);

		_ = result.Should().Be("false");
	}

	[Test]
	public void FormatValue_Int_ReturnsNumber()
	{
		var result = EsqlFormatting.FormatValue(42, ReaderOptions);

		_ = result.Should().Be("42");
	}

	[Test]
	public void FormatValue_NegativeInt_ReturnsNegativeNumber()
	{
		var result = EsqlFormatting.FormatValue(-42, ReaderOptions);

		_ = result.Should().Be("-42");
	}

	[Test]
	public void FormatValue_Double_ReturnsNumber()
	{
		var result = EsqlFormatting.FormatValue(3.14, ReaderOptions);

		_ = result.Should().Be("3.14");
	}

	[Test]
	public void FormatValue_DoubleNaN_ReturnsNull()
	{
		var result = EsqlFormatting.FormatValue(double.NaN, ReaderOptions);

		_ = result.Should().Be("null");
	}

	[Test]
	public void FormatValue_DoublePositiveInfinity_ReturnsNull()
	{
		var result = EsqlFormatting.FormatValue(double.PositiveInfinity, ReaderOptions);

		_ = result.Should().Be("null");
	}
}
