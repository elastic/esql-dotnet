// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>
/// Well-known serverless inference endpoint IDs for use with the COMPLETION command.
/// </summary>
public static class InferenceEndpoints
{
	public static class Anthropic
	{
		public const string Claude37Sonnet = ".anthropic-claude-3.7-sonnet-completion";
		public const string Claude45Opus = ".anthropic-claude-4.5-opus-completion";
		public const string Claude45Sonnet = ".anthropic-claude-4.5-sonnet-completion";
		public const string Claude46Opus = ".anthropic-claude-4.6-opus-completion";
	}

	public static class Google
	{
		public const string Gemini25Flash = ".google-gemini-2.5-flash-completion";
		public const string Gemini25Pro = ".google-gemini-2.5-pro-completion";
	}

	public static class Elastic
	{
		public const string GpLlmV2 = ".gp-llm-v2-completion";
	}

	public static class OpenAi
	{
		public const string Gpt41 = ".openai-gpt-4.1-completion";
		public const string Gpt41Mini = ".openai-gpt-4.1-mini-completion";
		public const string Gpt52 = ".openai-gpt-5.2-completion";
		public const string GptOss120B = ".openai-gpt-oss-120b-completion";
	}
}
