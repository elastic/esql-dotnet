// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class ParameterNameCollisionTests : EsqlTestBase
{
	[Test]
	public void ToEsqlString_Parameterized_SuffixCollision_KeepsAllValues()
	{
		var id = 5;
		var id_2 = 99;

		var queryable = (IEsqlQueryable<LogEntry>)CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= id_2)
			.Where(l => l.StatusCode > id)
			.Where(l => l.StatusCode < id);

		var esql = queryable.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= ?id_2
			| WHERE statusCode > ?id
			| WHERE statusCode < ?id_3
			""".NativeLineEndings());

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters!.Parameters.Should().HaveCount(3);
		_ = parameters.Parameters["id_2"].GetInt32().Should().Be(99);
		_ = parameters.Parameters["id"].GetInt32().Should().Be(5);
		_ = parameters.Parameters["id_3"].GetInt32().Should().Be(5);
	}
}
