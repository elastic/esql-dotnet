// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Formatting;

/// <summary>
/// Quoting rules for ES|QL identifiers. Column names and index patterns follow different
/// grammars: column names are backtick-quoted per path segment, while index patterns are
/// double-quoted as a whole when they contain characters invalid in an unquoted source target.
/// </summary>
internal static class EsqlIdentifier
{
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

	/// <summary>
	/// Escapes a (possibly dotted) column path for ES|QL. Each dot-separated segment is
	/// backtick-quoted independently when it is not a valid unquoted identifier, so a path
	/// like <c>user-agent.os name</c> renders as <c>`user-agent`.`os name`</c>.
	/// </summary>
	public static string EscapeColumnName(string path)
	{
		if (string.IsNullOrEmpty(path))
			return path;

		if (path.IndexOf('.') < 0)
			return EscapeColumnSegment(path);

		var segments = path.Split('.');
		for (var i = 0; i < segments.Length; i++)
			segments[i] = EscapeColumnSegment(segments[i]);

		return string.Join(".", segments);
	}

	private static string EscapeColumnSegment(string segment) =>
		IsValidUnquotedColumnSegment(segment)
			? segment
			: $"`{segment.Replace("`", "``")}`";

	private static bool IsValidUnquotedColumnSegment(string segment)
	{
		if (segment.Length == 0)
			return false;

		// Per the ES|QL grammar an unquoted identifier starts with a letter, '_' or '@'
		// and continues with letters, digits or '_'.
		var first = segment[0];
		if (!IsAsciiLetter(first) && first is not ('_' or '@'))
			return false;

		for (var i = 1; i < segment.Length; i++)
		{
			var c = segment[i];
			if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
				return false;
		}

		return !ReservedKeywords.Contains(segment);
	}

	/// <summary>
	/// Formats an index name or pattern for a FROM / LOOKUP JOIN target. Valid patterns
	/// (letters, digits, '-', '.', '*', '_', ':' for cross-cluster references and ',' for
	/// pattern lists) are emitted verbatim; anything else is double-quoted.
	/// </summary>
	public static string FormatIndexPattern(string pattern) =>
		IsValidUnquotedIndexPattern(pattern)
			? pattern
			: $"\"{pattern.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

	private static bool IsValidUnquotedIndexPattern(string pattern)
	{
		if (string.IsNullOrEmpty(pattern))
			return false;

		// An index literally named "metadata" collides with the METADATA directive that may
		// follow the FROM target, so it must be quoted.
		if (string.Equals(pattern, "metadata", StringComparison.OrdinalIgnoreCase))
			return false;

		foreach (var c in pattern)
		{
			if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c is not ('-' or '.' or '*' or '_' or ':' or ','))
				return false;
		}

		return true;
	}

	private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

	private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';
}
