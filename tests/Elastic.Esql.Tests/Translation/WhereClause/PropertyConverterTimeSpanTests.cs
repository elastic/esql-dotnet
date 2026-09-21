// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>A property-level converter on a <see cref="TimeSpan"/> member wins over the default duration literal.</summary>
public class PropertyConverterTimeSpanTests : EsqlTestBase
{
	[Test]
	public void ToEsqlString_Inline_TimeSpanWithPropertyConverter_UsesConverter()
	{
		var minimum = TimeSpan.FromSeconds(5);

		var esql = CreateQuery<ConvertedDurationDocument>()
			.From("docs-*")
			.Where(d => d.Duration > minimum)
			.ToEsqlString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE duration > 5000
			""".NativeLineEndings());
	}

	[Test]
	public void GetParameters_TimeSpanWithPropertyConverter_StoresConvertedValue()
	{
		var minimum = TimeSpan.FromSeconds(5);

		var queryable = (IEsqlQueryable<ConvertedDurationDocument>)CreateQuery<ConvertedDurationDocument>()
			.From("docs-*")
			.Where(d => d.Duration > minimum);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();

		var minimumParam = parameters.Parameters["minimum"];
		_ = minimumParam.ValueKind.Should().Be(JsonValueKind.Number);
		_ = minimumParam.GetInt64().Should().Be(5000);
	}
}
