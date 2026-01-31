// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Functions;

/// <summary>
/// Marker methods for ES|QL functions. These methods are not executed;
/// they are translated to ES|QL function calls during query translation.
/// </summary>
public static class EsqlFunctions
{
	// Date/Time Functions

	/// <summary>
	/// Returns the current date and time. Translates to NOW().
	/// </summary>
	public static DateTime Now() => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Truncates a date to the specified unit. Translates to DATE_TRUNC(unit, field).
	/// </summary>
	public static DateTime DateTrunc(string unit, DateTime field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Formats a date according to the pattern. Translates to DATE_FORMAT(field, pattern).
	/// </summary>
	public static string DateFormat(DateTime field, string pattern) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// String Functions

	/// <summary>
	/// Returns the length of a string. Translates to LENGTH(field).
	/// </summary>
	public static int Length(string field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Returns a substring. Translates to SUBSTRING(field, start).
	/// </summary>
	public static string Substring(string field, int start) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Returns a substring. Translates to SUBSTRING(field, start, length).
	/// </summary>
	public static string Substring(string field, int start, int length) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Trims whitespace. Translates to TRIM(field).
	/// </summary>
	public static string Trim(string field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Converts to lowercase. Translates to TO_LOWER(field).
	/// </summary>
	public static string ToLower(string field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Converts to uppercase. Translates to TO_UPPER(field).
	/// </summary>
	public static string ToUpper(string field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Concatenates strings. Translates to CONCAT(a, b, ...).
	/// </summary>
	public static string Concat(params string[] values) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// Pattern Matching

	/// <summary>
	/// Performs a full-text match. Translates to MATCH(field, query).
	/// </summary>
	public static bool Match(string field, string query) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Performs a LIKE pattern match. Translates to field LIKE pattern.
	/// </summary>
	public static bool Like(string field, string pattern) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Performs a regex pattern match. Translates to field RLIKE pattern.
	/// </summary>
	public static bool Rlike(string field, string pattern) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// Null Handling

	/// <summary>
	/// Checks if a value is null. Translates to field IS NULL.
	/// </summary>
	public static bool IsNull<T>(T field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Checks if a value is not null. Translates to field IS NOT NULL.
	/// </summary>
	public static bool IsNotNull<T>(T field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Returns the first non-null value. Translates to COALESCE(a, b, ...).
	/// </summary>
	public static T Coalesce<T>(params T[] values) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// Math Functions

	/// <summary>
	/// Returns the absolute value. Translates to ABS(field).
	/// </summary>
	public static double Abs(double field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Returns the ceiling. Translates to CEIL(field).
	/// </summary>
	public static double Ceil(double field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Returns the floor. Translates to FLOOR(field).
	/// </summary>
	public static double Floor(double field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Rounds a number. Translates to ROUND(field).
	/// </summary>
	public static double Round(double field) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	/// <summary>
	/// Rounds a number to decimals. Translates to ROUND(field, decimals).
	/// </summary>
	public static double Round(double field, int decimals) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// IP Functions

	/// <summary>
	/// Checks if an IP matches a CIDR range. Translates to CIDR_MATCH(ip, cidr).
	/// </summary>
	public static bool CidrMatch(string ip, string cidr) => throw new InvalidOperationException("This method should only be used in LINQ expressions.");
}
