// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class EnumComparisonTests : EsqlTestBase
{
	[Test]
	public void Where_ReversedEnumLessThan_MirrorsOperator()
	{
		var minPriority = Priority.Medium;

		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => minPriority < d.Priority)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE priority > 1
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ReversedEnumGreaterThanOrEqual_MirrorsOperator()
	{
		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => Priority.High >= d.Priority)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE priority <= 2
			""".NativeLineEndings());
	}

	[Test]
	public void Where_EnumLessThan_MemberFirst_KeepsOperator()
	{
		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => d.Priority < Priority.High)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE priority < 2
			""".NativeLineEndings());
	}
}
