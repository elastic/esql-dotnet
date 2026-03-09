// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using static System.Globalization.CultureInfo;

namespace Elastic.Esql.Formatting;

/// <summary>
/// Maps C# types to ES|QL types and formats values.
/// </summary>
internal static class EsqlFormatting
{
	/// <summary>
	/// Formats a C# value for use in an ES|QL query literal. Types with ES|QL-specific
	/// formatting (DateTime, TimeSpan, float/double NaN) are handled explicitly; all other
	/// types are serialized via <see cref="JsonSerializer"/> using the provided options.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	public static string FormatValue(object? value, JsonSerializerOptions options) =>
		value switch
		{
			null => "null",
			string s => FormatString(s),
			bool b => b ? "true" : "false",
			DateTime dt => FormatDateTime(dt),
			DateTimeOffset dto => FormatDateTime(dto.UtcDateTime),
#if NET6_0_OR_GREATER
			DateOnly d => $"\"{d:yyyy-MM-dd}\"",
			TimeOnly t => $"\"{t:HH:mm:ss}\"",
#endif
			TimeSpan ts => FormatTimeSpan(ts),
			float f => FormatFloat(f),
			double d => FormatDouble(d),
			_ => FormatJsonElement(
				JsonSerializer.SerializeToElement(value, value.GetType(), options))
		};

	/// <summary>
	/// Converts a <see cref="JsonElement"/> to an ES|QL literal string.
	/// String values are escaped via <see cref="FormatString"/>.
	/// </summary>
	internal static string FormatJsonElement(JsonElement element) =>
		element.ValueKind switch
		{
			JsonValueKind.String => FormatString(element.GetString()!),
			JsonValueKind.Number => element.GetRawText(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Null or JsonValueKind.Undefined => "null",
			_ => throw new NotSupportedException(
				$"JsonValueKind '{element.ValueKind}' is not supported as an ES|QL value.")
		};

	internal static string FormatString(string s)
	{
		var escaped = s
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\n", "\\n")
			.Replace("\r", "\\r")
			.Replace("\t", "\\t");
		return $"\"{escaped}\"";
	}

	/// <summary>
	/// Returns the ES|QL duration string for a <see cref="TimeSpan"/> (e.g. <c>3 days</c>).
	/// </summary>
	internal static string FormatTimeSpanRaw(TimeSpan ts)
	{
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

	private static string FormatDateTime(DateTime dt) =>
		$"\"{dt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}\"";

	private static string FormatTimeSpan(TimeSpan ts) =>
		FormatTimeSpanRaw(ts);

	private static string FormatFloat(float f) =>
		float.IsNaN(f) || float.IsInfinity(f)
			? "null"
			: f.ToString("G", InvariantCulture);

	private static string FormatDouble(double d) =>
		double.IsNaN(d) || double.IsInfinity(d)
			? "null"
			: d.ToString("G", InvariantCulture);
}
