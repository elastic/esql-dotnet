// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Formatting;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql;

/// <summary>
/// Static factory for standalone ROW + COMPLETION queries.
/// </summary>
public static class CompletionQuery
{
	/// <summary>
	/// Generates an ES|QL query string for a standalone ROW + COMPLETION pipeline.
	/// </summary>
	/// <param name="prompt">The prompt text.</param>
	/// <param name="inferenceId">The inference endpoint ID.</param>
	/// <param name="column">Optional output column name.</param>
	public static string Generate(string prompt, string inferenceId, string? column = null)
	{
		var escapedPrompt = EsqlFormatting.FormatValue(prompt);
		var query = new EsqlQuery();
		query.AddCommand(new RowCommand($"prompt = {escapedPrompt}"));
		query.AddCommand(new CompletionCommand("prompt", inferenceId, column));

		var generator = new EsqlGenerator();
		return generator.Generate(query);
	}
}
