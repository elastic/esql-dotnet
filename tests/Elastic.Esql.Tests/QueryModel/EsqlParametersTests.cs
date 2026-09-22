// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.QueryModel;

public class EsqlParametersTests
{
	[Test]
	public void Add_NewName_ReturnsPreferredName()
	{
		var parameters = new EsqlParameters();

		var name = parameters.Add("threshold", JsonSerializer.SerializeToElement(500));

		_ = name.Should().Be("threshold");
		_ = parameters.Parameters["threshold"].GetInt32().Should().Be(500);
	}

	[Test]
	public void Add_DuplicateName_ReturnsSuffixedName()
	{
		var parameters = new EsqlParameters();

		_ = parameters.Add("threshold", JsonSerializer.SerializeToElement(100));
		var second = parameters.Add("threshold", JsonSerializer.SerializeToElement(200));

		_ = second.Should().Be("threshold_2");
		_ = parameters.Parameters["threshold_2"].GetInt32().Should().Be(200);
	}

	[Test]
	public void Add_EmptyName_ThrowsArgumentException()
	{
		var parameters = new EsqlParameters();

		var act = () => parameters.Add("", JsonSerializer.SerializeToElement(1));

		_ = act.Should().Throw<ArgumentException>();
	}

	[Test]
	public void Add_NullName_ThrowsArgumentNullException()
	{
		var parameters = new EsqlParameters();

		var act = () => parameters.Add(null!, JsonSerializer.SerializeToElement(1));

		_ = act.Should().Throw<ArgumentNullException>();
	}

	[Test]
	[Arguments("  ")]
	[Arguments("2fast")]
	[Arguments("my-param")]
	[Arguments("größe")]
	public void Add_NameOutsideEsqlGrammar_ThrowsArgumentException(string name)
	{
		var parameters = new EsqlParameters();

		var act = () => parameters.Add(name, JsonSerializer.SerializeToElement(1));

		_ = act.Should().Throw<ArgumentException>().WithMessage("*letter or underscore*").WithParameterName("preferredName");
	}

	[Test]
	public void Add_ElementFromDisposedDocument_RemainsReadable()
	{
		var parameters = new EsqlParameters();

		using (var document = JsonDocument.Parse("42"))
			_ = parameters.Add("answer", document.RootElement);

		_ = parameters.Parameters["answer"].GetInt32().Should().Be(42);
	}

	[Test]
	public void Add_UnderscoreLeadingNameWithDigits_IsAccepted()
	{
		var parameters = new EsqlParameters();

		var name = parameters.Add("_p2", JsonSerializer.SerializeToElement(1));

		_ = name.Should().Be("_p2");
	}
}
