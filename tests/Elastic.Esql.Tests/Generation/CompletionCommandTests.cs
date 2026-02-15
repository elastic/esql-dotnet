// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.Generation;

public class CompletionCommandTests
{
	private readonly EsqlGenerator _generator = new();

	[Test]
	public void Generate_CompletionWithColumn_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));
		query.AddCommand(new CompletionCommand("message", ".openai-gpt-4.1-completion", "analysis"));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            FROM logs-*
            | COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            """);
	}

	[Test]
	public void Generate_CompletionWithoutColumn_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));
		query.AddCommand(new CompletionCommand("message", ".openai-gpt-4.1-completion"));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            FROM logs-*
            | COMPLETION message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            """);
	}

	[Test]
	public void Generate_RowWithSingleExpression_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new RowCommand("prompt = \"Hello\""));

		var result = _generator.Generate(query);

		_ = result.Should().Be("ROW prompt = \"Hello\"");
	}

	[Test]
	public void Generate_RowWithMultipleExpressions_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new RowCommand("a = 1", "b = \"two\""));

		var result = _generator.Generate(query);

		_ = result.Should().Be("ROW a = 1, b = \"two\"");
	}

	[Test]
	public void Generate_RowAndCompletion_GeneratesPipeline()
	{
		var query = new EsqlQuery();
		query.AddCommand(new RowCommand("prompt = \"Tell me about Elasticsearch\""));
		query.AddCommand(new CompletionCommand("prompt", ".openai-gpt-4.1-completion", "answer"));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            ROW prompt = "Tell me about Elasticsearch"
            | COMPLETION answer = prompt WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            """);
	}

	[Test]
	public void Generate_FullRagPipeline_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery();
		query.AddCommand(new FromCommand("logs-*"));
		query.AddCommand(new WhereCommand("level == \"ERROR\""));
		query.AddCommand(new SortCommand(new SortField("@timestamp", true)));
		query.AddCommand(new LimitCommand(10));
		query.AddCommand(new EvalCommand("prompt = CONCAT(\"Summarize: \", message)"));
		query.AddCommand(new CompletionCommand("prompt", ".openai-gpt-4.1-completion", "summary"));
		query.AddCommand(new KeepCommand("message", "summary"));

		var result = _generator.Generate(query);

		_ = result.Should().Be(
			"""
            FROM logs-*
            | WHERE level == "ERROR"
            | SORT @timestamp DESC
            | LIMIT 10
            | EVAL prompt = CONCAT("Summarize: ", message)
            | COMPLETION summary = prompt WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            | KEEP message, summary
            """);
	}
}
