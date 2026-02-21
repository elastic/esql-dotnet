// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.Generation;

public class CompletionCommandTests
{
	private readonly EsqlFormatter _formatter = new();

	[Test]
	public void Generate_CompletionWithColumn_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new FromCommand("logs-*"),
			new CompletionCommand("message", ".openai-gpt-4.1-completion", "analysis")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be(
			"""
			FROM logs-*
			| COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
			""".NativeLineEndings());
	}

	[Test]
	public void Generate_CompletionWithoutColumn_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new FromCommand("logs-*"),
			new CompletionCommand("message", ".openai-gpt-4.1-completion")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be(
			"""
			FROM logs-*
			| COMPLETION message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
			""".NativeLineEndings());
	}

	[Test]
	public void Generate_RowWithSingleExpression_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new RowCommand("prompt = \"Hello\"")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be("ROW prompt = \"Hello\"");
	}

	[Test]
	public void Generate_RowWithMultipleExpressions_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new RowCommand("a = 1", "b = \"two\"")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be("ROW a = 1, b = \"two\"");
	}

	[Test]
	public void Generate_RowAndCompletion_GeneratesPipeline()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new RowCommand("prompt = \"Tell me about Elasticsearch\""),
			new CompletionCommand("prompt", ".openai-gpt-4.1-completion", "answer")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be(
			"""
			ROW prompt = "Tell me about Elasticsearch"
			| COMPLETION answer = prompt WITH { "inference_id" : ".openai-gpt-4.1-completion" }
			""".NativeLineEndings());
	}

	[Test]
	public void Generate_FullRagPipeline_GeneratesCorrectEsql()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new FromCommand("logs-*"),
			new WhereCommand("level == \"ERROR\""),
			new SortCommand(new SortField("@timestamp", true)),
			new LimitCommand(10),
			new EvalCommand("prompt = CONCAT(\"Summarize: \", message)"),
			new CompletionCommand("prompt", ".openai-gpt-4.1-completion", "summary"),
			new KeepCommand("message", "summary")
		], null);

		var result = _formatter.Format(query);

		_ = result.Should().Be(
			"""
			FROM logs-*
			| WHERE level == "ERROR"
			| SORT @timestamp DESC
			| LIMIT 10
			| EVAL prompt = CONCAT("Summarize: ", message)
			| COMPLETION summary = prompt WITH { "inference_id" : ".openai-gpt-4.1-completion" }
			| KEEP message, summary
			""".NativeLineEndings());
	}
}
