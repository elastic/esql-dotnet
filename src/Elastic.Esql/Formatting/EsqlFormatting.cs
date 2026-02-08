// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using static System.Globalization.CultureInfo;

namespace Elastic.Esql.Formatting;

/// <summary>
/// Maps C# types to ES|QL types and formats values.
/// </summary>
public static class EsqlFormatting
{
	/// <summary>
	/// Formats a C# value for use in an ES|QL query.
	/// </summary>
	public static string FormatValue(object? value) =>
		value switch
		{
			null => "null",
			string s => FormatString(s),
			bool b => b ? "true" : "false",
			DateTime dt => FormatDateTime(dt),
			DateTimeOffset dto => FormatDateTime(dto.UtcDateTime),
			DateOnly d => $"\"{d:yyyy-MM-dd}\"",
			TimeOnly t => $"\"{t:HH:mm:ss}\"",
			TimeSpan ts => FormatTimeSpan(ts),
			char c => FormatString(c.ToString()),
			byte or sbyte or short or ushort or int or uint or long or ulong => value.ToString()!,
			float f => FormatFloat(f),
			double d => FormatDouble(d),
			decimal m => m.ToString(InvariantCulture),
			Enum e => FormatString(e.ToString()),
			Guid g => FormatString(g.ToString()),
			_ => FormatString(value.ToString() ?? "")
		};

	private static string FormatString(string s)
	{
		// Escape special characters in ES|QL strings
		var escaped = s
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\n", "\\n")
			.Replace("\r", "\\r")
			.Replace("\t", "\\t");
		return $"\"{escaped}\"";
	}

	private static string FormatDateTime(DateTime dt) =>
		// ES|QL expects ISO 8601 format
		$"\"{dt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}\"";

	private static string FormatTimeSpan(TimeSpan ts)
	{
		// Convert to ES|QL duration format
		if (ts.TotalDays >= 1)
			return $"{(long)ts.TotalDays} days";
		if (ts.TotalHours >= 1)
			return $"{(long)ts.TotalHours} hours";
		if (ts.TotalMinutes >= 1)
			return $"{(long)ts.TotalMinutes} minutes";
		if (ts.TotalSeconds >= 1)
			return $"{(long)ts.TotalSeconds} seconds";
		return $"{(long)ts.TotalMilliseconds} milliseconds";
	}

	private static string FormatFloat(float f)
	{
		if (float.IsNaN(f))
			return "null";
		if (float.IsPositiveInfinity(f))
			return "null";
		if (float.IsNegativeInfinity(f))
			return "null";
		return f.ToString("G", InvariantCulture);
	}

	private static string FormatDouble(double d)
	{
		if (double.IsNaN(d))
			return "null";
		if (double.IsPositiveInfinity(d))
			return "null";
		if (double.IsNegativeInfinity(d))
			return "null";
		return d.ToString("G", InvariantCulture);
	}
}
