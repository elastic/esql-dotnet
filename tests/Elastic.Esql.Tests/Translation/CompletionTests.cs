// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class CompletionTests : EsqlTestBase
{
	[Test]
	public void Completion_WithStringPrompt_GeneratesCompletion()
	{
		var esql = Client.Query<LogEntry>()
			.Completion("message", InferenceEndpoints.OpenAi.Gpt41)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | COMPLETION message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            """);
	}

	[Test]
	public void Completion_WithLambdaPrompt_GeneratesCompletion()
	{
		var esql = Client.Query<LogEntry>()
			.Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | COMPLETION message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            """);
	}

	[Test]
	public void Completion_WithColumn_GeneratesColumnAssignment()
	{
		var esql = Client.Query<LogEntry>()
			.Completion(l => l.Message, InferenceEndpoints.Anthropic.Claude46Opus, column: "analysis")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | COMPLETION analysis = message WITH { "inference_id" : ".anthropic-claude-4.6-opus-completion" }
            """);
	}

	[Test]
	public void Completion_WithPipeline_GeneratesCorrectOrder()
	{
		var esql = Client.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.Take(1)
			.Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")
			.Keep("message", "analysis")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE statusCode >= 500
            | LIMIT 1
            | COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
            | KEEP message, analysis
            """);
	}

	[Test]
	public void CompletionQuery_Generate_WithColumn_GeneratesRowAndCompletion()
	{
		var esql = CompletionQuery.Generate(
			"Tell me about Elasticsearch",
			InferenceEndpoints.Anthropic.Claude46Opus,
			column: "answer"
		);

		_ = esql.Should().Be(
			"""
            ROW prompt = "Tell me about Elasticsearch"
            | COMPLETION answer = prompt WITH { "inference_id" : ".anthropic-claude-4.6-opus-completion" }
            """);
	}

	[Test]
	public void CompletionQuery_Generate_WithoutColumn_GeneratesRowAndCompletion()
	{
		var esql = CompletionQuery.Generate(
			"Tell me about Elasticsearch",
			InferenceEndpoints.Google.Gemini25Pro
		);

		_ = esql.Should().Be(
			"""
            ROW prompt = "Tell me about Elasticsearch"
            | COMPLETION prompt WITH { "inference_id" : ".google-gemini-2.5-pro-completion" }
            """);
	}

	[Test]
	public void CompletionQuery_Generate_EscapesPrompt()
	{
		var esql = CompletionQuery.Generate(
			"What does \"hello world\" mean?",
			InferenceEndpoints.Elastic.GpLlmV2
		);

		_ = esql.Should().Be(
			"""
            ROW prompt = "What does \"hello world\" mean?"
            | COMPLETION prompt WITH { "inference_id" : ".gp-llm-v2-completion" }
            """);
	}

	[Test]
	public void Completion_WithJsonPropertyNameField_ResolvesFieldName()
	{
		var esql = Client.Query<LogEntry>()
			.Completion(l => l.Level, InferenceEndpoints.OpenAi.Gpt41Mini, column: "result")
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | COMPLETION result = log.level WITH { "inference_id" : ".openai-gpt-4.1-mini-completion" }
            """);
	}

	[Test]
	public void InferenceEndpoints_Constants_HaveCorrectValues()
	{
		_ = InferenceEndpoints.Anthropic.Claude37Sonnet.Should().Be(".anthropic-claude-3.7-sonnet-completion");
		_ = InferenceEndpoints.Anthropic.Claude45Opus.Should().Be(".anthropic-claude-4.5-opus-completion");
		_ = InferenceEndpoints.Anthropic.Claude45Sonnet.Should().Be(".anthropic-claude-4.5-sonnet-completion");
		_ = InferenceEndpoints.Anthropic.Claude46Opus.Should().Be(".anthropic-claude-4.6-opus-completion");
		_ = InferenceEndpoints.Google.Gemini25Flash.Should().Be(".google-gemini-2.5-flash-completion");
		_ = InferenceEndpoints.Google.Gemini25Pro.Should().Be(".google-gemini-2.5-pro-completion");
		_ = InferenceEndpoints.Elastic.GpLlmV2.Should().Be(".gp-llm-v2-completion");
		_ = InferenceEndpoints.OpenAi.Gpt41.Should().Be(".openai-gpt-4.1-completion");
		_ = InferenceEndpoints.OpenAi.Gpt41Mini.Should().Be(".openai-gpt-4.1-mini-completion");
		_ = InferenceEndpoints.OpenAi.Gpt52.Should().Be(".openai-gpt-5.2-completion");
		_ = InferenceEndpoints.OpenAi.GptOss120B.Should().Be(".openai-gpt-oss-120b-completion");
	}
}
