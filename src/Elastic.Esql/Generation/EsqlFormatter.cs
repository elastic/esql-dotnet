// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Esql.Core;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Generation;

/// <summary>
/// Generates ES|QL query strings from query models.
/// </summary>
internal sealed class EsqlFormatter : ICommandVisitor
{
	private readonly StringBuilder _builder = new();
	private bool _isFirstCommand = true;

	/// <summary>
	/// Generates an ES|QL query string from a query model.
	/// </summary>
	public string Format(EsqlQuery query)
	{
		_ = _builder.Clear();
		_isFirstCommand = true;

		foreach (var command in query.Commands)
			command.Accept(this);

		return _builder.ToString();
	}

	private void AppendCommand(string command)
	{
		if (_isFirstCommand)
		{
			_ = _builder.Append(command);
			_isFirstCommand = false;
		}
		else
		{
			_ = _builder.AppendLine();
			_ = _builder.Append("| ");
			_ = _builder.Append(command);
		}
	}

	public void Visit(FromCommand command)
	{
		var from = $"FROM {EscapeIdentifier(command.IndexPattern)}";

		if (command.Metadata != MetadataField.None)
			from = $"{from} METADATA {string.Join(", ", MetadataFieldHelper.EnumerateNames(command.Metadata))}";

		AppendCommand(from);
	}

	public void Visit(WhereCommand command) => AppendCommand($"WHERE {command.Condition}");

	public void Visit(EvalCommand command)
	{
		if (command.Expressions.Count > 0)
			AppendCommand($"EVAL {string.Join(", ", command.Expressions)}");
	}

	public void Visit(StatsCommand command)
	{
		var stats = new StringBuilder("STATS ");
		_ = stats.Append(string.Join(", ", command.Aggregations));

		if (command.GroupBy != null && command.GroupBy.Count > 0)
		{
			_ = stats.Append(" BY ");
			_ = stats.Append(string.Join(", ", command.GroupBy));
		}

		AppendCommand(stats.ToString());
	}

	public void Visit(SortCommand command)
	{
		var fields = command.Fields.Select(f =>
			f.Descending ? $"{f.FieldName} DESC" : f.FieldName);

		AppendCommand($"SORT {string.Join(", ", fields)}");
	}

	public void Visit(LimitCommand command) => AppendCommand($"LIMIT {command.Count}");

	public void Visit(KeepCommand command)
	{
		if (command.Fields.Count > 0)
			AppendCommand($"KEEP {string.Join(", ", command.Fields)}");
	}

	public void Visit(DropCommand command)
	{
		if (command.Fields.Count > 0)
			AppendCommand($"DROP {string.Join(", ", command.Fields)}");
	}

	public void Visit(CompletionCommand command)
	{
		var columnAssignment = command.Column != null ? $"{command.Column} = " : "";
		var escapedId = command.InferenceId.Replace("\\", "\\\\").Replace("\"", "\\\"");
		AppendCommand($"COMPLETION {columnAssignment}{command.Prompt} WITH {{ \"inference_id\" : \"{escapedId}\" }}");
	}

	public void Visit(RowCommand command)
	{
		if (command.Expressions.Count > 0)
			AppendCommand($"ROW {string.Join(", ", command.Expressions)}");
	}

	public void Visit(RenameCommand command)
	{
		if (command.Fields.Count > 0)
			AppendCommand($"RENAME {string.Join(", ", command.Fields.Select(f => $"{EscapeIdentifier(f.OldName)} AS {EscapeIdentifier(f.NewName)}"))}");
	}

	public void Visit(LookupJoinCommand command) =>
		AppendCommand($"LOOKUP JOIN {EscapeIdentifier(command.LookupIndex)} ON {command.OnCondition}");

	public void Visit(RawFragmentCommand command) => AppendCommand(command.Fragment);

	public void Visit(ForkCommand command)
	{
		if (command.Branches.Count == 0)
			return;

		var sb = new StringBuilder("FORK");
		var first = true;

		foreach (var branch in command.Branches)
		{
			if (branch.Count == 0)
				continue;

			_ = sb.Append(first ? " (" : " (");
			first = false;

			for (var i = 0; i < branch.Count; i++)
			{
				if (i > 0)
					_ = sb.Append(" | ");
				_ = sb.Append(branch[i]);
			}

			_ = sb.Append(')');
		}

		AppendCommand(sb.ToString());
	}

	public void Visit(FuseCommand command)
	{
		var sb = new StringBuilder("FUSE");

		if (command.Method == FuseMethod.Linear)
			_ = sb.Append(" LINEAR");

		if (command.ScoreColumn is not null)
			_ = sb.Append(" SCORE BY ").Append(command.ScoreColumn);

		if (command.GroupColumn is not null)
			_ = sb.Append(" GROUP BY ").Append(command.GroupColumn);

		if (command.KeyColumns is { Count: > 0 } keys)
			_ = sb.Append(" KEY BY ").Append(string.Join(", ", keys));

		var withParts = new List<string>();
		if (command.Method == FuseMethod.Rrf && command.RankConstant is { } rc)
			withParts.Add($"\"rank_constant\": {rc.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

		if (command.Method == FuseMethod.Linear && command.Normalizer == ScoreNormalizer.MinMax)
			withParts.Add("\"normalizer\": \"minmax\"");

		if (command.Weights is { Count: > 0 } w)
		{
			var weightItems = w
				.Select((value, idx) => $"\"fork{idx + 1}\": {value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}");
			withParts.Add($"\"weights\": {{ {string.Join(", ", weightItems)} }}");
		}

		if (withParts.Count > 0)
			_ = sb.Append(" WITH { ").Append(string.Join(", ", withParts)).Append(" }");

		AppendCommand(sb.ToString());
	}

	/// <summary>
	/// Escapes an identifier for ES|QL if needed.
	/// </summary>
	private static string EscapeIdentifier(string identifier)
	{
		// Index patterns with wildcards don't need escaping
		if (identifier.Contains('*') || identifier.Contains('?'))
			return identifier;

		// Check if identifier needs escaping (contains special characters)
		if (NeedsEscaping(identifier))
			return $"`{identifier.Replace("`", "``")}`";

		return identifier;
	}

	private static bool NeedsEscaping(string identifier)
	{
		if (string.IsNullOrEmpty(identifier))
			return false;

		// Check for characters that require escaping
		foreach (var c in identifier)
		{
			if (!char.IsLetterOrDigit(c) && c != '_' && c != '.' && c != '-' && c != '*' && c != '?' && c != '@')
				return true;
		}

		// Check if it starts with a digit
		if (char.IsDigit(identifier[0]))
			return true;

		// Check if it's a reserved keyword
		if (IsReservedKeyword(identifier))
			return true;

		return false;
	}

	private static readonly HashSet<string> ReservedKeywords = new(
		[
			"FROM", "WHERE", "EVAL", "STATS", "SORT", "LIMIT", "KEEP", "DROP",
			"BY", "AS", "AND", "OR", "NOT", "IN", "LIKE", "RLIKE", "IS", "NULL",
			"TRUE", "FALSE", "ASC", "DESC", "NULLS", "FIRST", "LAST",
			"ROW", "SHOW", "META", "METADATA", "MV_EXPAND", "RENAME", "DISSECT", "GROK", "ENRICH",
			"COMPLETION", "JOIN", "LOOKUP"
		],
		StringComparer.OrdinalIgnoreCase
	);

	private static bool IsReservedKeyword(string identifier) => ReservedKeywords.Contains(identifier);
}
