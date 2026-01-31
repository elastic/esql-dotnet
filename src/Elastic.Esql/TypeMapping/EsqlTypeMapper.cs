// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using static System.Globalization.CultureInfo;

namespace Elastic.Esql.TypeMapping;

/// <summary>
/// Maps C# types to ES|QL types and formats values.
/// </summary>
public static class EsqlTypeMapper
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

	/// <summary>
	/// Gets the ES|QL type name for a C# type.
	/// </summary>
	public static string GetEsqlTypeName(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type) ?? type;

		return underlying switch
		{
			_ when underlying == typeof(string) => "keyword",
			_ when underlying == typeof(bool) => "boolean",
			_ when underlying == typeof(int) || underlying == typeof(long) => "long",
			_ when underlying == typeof(short) || underlying == typeof(byte) => "integer",
			_ when underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal) => "double",
			_ when underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) => "date",
			_ when underlying == typeof(Guid) => "keyword",
			_ when underlying.IsEnum => "keyword",
			_ => "keyword"
		};
	}
}
