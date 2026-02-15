// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.QueryModel.Commands;

/// <summary>
/// Represents the COMPLETION command for LLM inference within ES|QL queries.
/// </summary>
public class CompletionCommand(string prompt, string inferenceId, string? column = null) : QueryCommand
{
	/// <summary>The field reference or expression for the prompt.</summary>
	public string Prompt { get; } = prompt ?? throw new ArgumentNullException(nameof(prompt));

	/// <summary>The inference endpoint ID.</summary>
	public string InferenceId { get; } = inferenceId ?? throw new ArgumentNullException(nameof(inferenceId));

	/// <summary>Optional output column name (ES|QL defaults to "completion").</summary>
	public string? Column { get; } = column;

	public override void Accept(ICommandVisitor visitor) => visitor.Visit(this);
}
