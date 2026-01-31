// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.Generation;

public class CommandGenerationTests
{
	private readonly EsqlGenerator _generator = new();

	[Test]
	public void Generate_FromCommand_GeneratesFrom()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));

		var result = _generator.Generate(query);

		_ = result.Should().Be("FROM logs-*");
	}

	[Test]
	public void Generate_FromAndWhere_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));
		query.AddCommand(new WhereCommand("level == \"ERROR\""));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            FROM logs-*
            | WHERE level == "ERROR"
            """);
	}

	[Test]
	public void Generate_CompleteQuery_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));
		query.AddCommand(new WhereCommand("level == \"ERROR\""));
		query.AddCommand(new SortCommand(new SortField("@timestamp", true)));
		query.AddCommand(new LimitCommand(100));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            FROM logs-*
            | WHERE level == "ERROR"
            | SORT @timestamp DESC
            | LIMIT 100
            """);
	}
}
