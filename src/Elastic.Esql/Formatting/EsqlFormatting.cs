// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text;
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
			DateOnly d => $"\"{d.ToString("yyyy-MM-dd", InvariantCulture)}\"",
			TimeOnly t => $"\"{t.ToString("HH:mm:ss", InvariantCulture)}\"",
#endif
			TimeSpan ts => FormatTimeSpan(ts),
			float f => FormatFloat(f),
			double d => FormatDouble(d),
			DenseVector<float> v => FormatFloatVector(v.Span),
			DenseVector<byte> v => FormatByteVector(v.Span),
			_ => FormatJsonElement(
				JsonSerializer.SerializeToElement(value, value.GetType(), options))
		};

	internal static string FormatFloatVector(ReadOnlySpan<float> span)
	{
		// Validate finite values up front; ES|QL has no representation for NaN / Infinity in a vector.
		for (var i = 0; i < span.Length; i++)
		{
			if (float.IsNaN(span[i]) || float.IsInfinity(span[i]))
				throw new ArgumentException(
					$"Vector element at index {i} is NaN or Infinity, which cannot be expressed in ES|QL.",
					nameof(span));
		}

		var sb = new StringBuilder("[");
		for (var i = 0; i < span.Length; i++)
		{
			if (i > 0)
				_ = sb.Append(", ");
			_ = sb.Append(FormatFloat(span[i]));
		}
		_ = sb.Append(']');
		return sb.ToString();
	}

	internal static string FormatByteVector(ReadOnlySpan<byte> span)
	{
		// ES|QL byte / bit vectors are wire-encoded as signed-byte numbers in [-128, 127].
		var sb = new StringBuilder("[");
		for (var i = 0; i < span.Length; i++)
		{
			if (i > 0)
				_ = sb.Append(", ");
			_ = sb.Append(((sbyte)span[i]).ToString(InvariantCulture));
		}
		_ = sb.Append(']');
		return sb.ToString();
	}

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
		if (ts.Ticks % TimeSpan.TicksPerDay == 0)
			return $"{(ts.Ticks / TimeSpan.TicksPerDay).ToString(InvariantCulture)} days";
		if (ts.Ticks % TimeSpan.TicksPerHour == 0)
			return $"{(ts.Ticks / TimeSpan.TicksPerHour).ToString(InvariantCulture)} hours";
		if (ts.Ticks % TimeSpan.TicksPerMinute == 0)
			return $"{(ts.Ticks / TimeSpan.TicksPerMinute).ToString(InvariantCulture)} minutes";
		if (ts.Ticks % TimeSpan.TicksPerSecond == 0)
			return $"{(ts.Ticks / TimeSpan.TicksPerSecond).ToString(InvariantCulture)} seconds";
		if (ts.Ticks % TimeSpan.TicksPerMillisecond == 0)
			return $"{(ts.Ticks / TimeSpan.TicksPerMillisecond).ToString(InvariantCulture)} milliseconds";

		return $"{ts.TotalMilliseconds.ToString("0.###", InvariantCulture)} milliseconds";
	}

	/// <summary>
	/// Formats a <see cref="DateTime"/> as an invariant UTC ISO-8601 literal.
	/// Kind policy: Utc is emitted as-is, Local is converted to UTC, and Unspecified is treated
	/// as UTC without conversion so the query text does not depend on the machine time zone.
	/// </summary>
	private static string FormatDateTime(DateTime dt)
	{
		var utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
		return $"\"{utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", InvariantCulture)}\"";
	}

	private static string FormatTimeSpan(TimeSpan ts) =>
		FormatTimeSpanRaw(ts);

	private static string FormatFloat(float f) =>
		float.IsNaN(f) || float.IsInfinity(f)
			? "null"
			: WithExplicitFloatingPoint(f.ToString("G9", InvariantCulture));

	private static string FormatDouble(double d) =>
		double.IsNaN(d) || double.IsInfinity(d)
			? "null"
			: WithExplicitFloatingPoint(d.ToString("G", InvariantCulture));

	/// <summary>
	/// A whole-number double like 100.0 renders as "100" under "G", which ES|QL parses as an
	/// integer literal; integer division then truncates silently. Keep the type explicit.
	/// </summary>
	private static string WithExplicitFloatingPoint(string text) =>
		text.IndexOfAny(['.', 'e', 'E']) < 0 ? $"{text}.0" : text;
}
