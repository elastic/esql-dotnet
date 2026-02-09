// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#pragma warning disable IDE0060 // All parameters are intentionally unused — these are marker methods for expression tree translation

namespace Elastic.Esql.Functions;

/// <summary>
/// Marker methods for ES|QL functions. These methods are not executed;
/// they are translated to ES|QL function calls during query translation.
/// </summary>
public static class EsqlFunctions
{
	private static T Throw<T>() => throw new InvalidOperationException("This method should only be used in LINQ expressions.");

	// ============================================================================
	// Date/Time Functions
	// ============================================================================

	/// <summary>Returns the current date and time. Translates to NOW().</summary>
	public static DateTime Now() => Throw<DateTime>();

	/// <summary>Truncates a date to the specified unit. Translates to DATE_TRUNC(unit, field).</summary>
	public static DateTime DateTrunc(string unit, DateTime field) => Throw<DateTime>();

	/// <summary>Formats a date according to the pattern. Translates to DATE_FORMAT(field, pattern).</summary>
	public static string DateFormat(DateTime field, string pattern) => Throw<string>();

	/// <summary>Returns the difference between two dates. Translates to DATE_DIFF(unit, start, end).</summary>
	public static int DateDiff(string unit, DateTime start, DateTime end) => Throw<int>();

	/// <summary>Parses a date string using the specified pattern. Translates to DATE_PARSE(pattern, dateString).</summary>
	public static DateTime DateParse(string pattern, string dateString) => Throw<DateTime>();

	/// <summary>Returns the day name. Translates to DAY_NAME(date).</summary>
	public static string DayName(DateTime date) => Throw<string>();

	/// <summary>Returns the month name. Translates to MONTH_NAME(date).</summary>
	public static string MonthName(DateTime date) => Throw<string>();

	/// <summary>Creates a time range. Translates to TRANGE(start, end).</summary>
	public static object TRange(DateTime start, DateTime end) => Throw<object>();

	// ============================================================================
	// String Functions
	// ============================================================================

	/// <summary>Returns the length of a string. Translates to LENGTH(field).</summary>
	public static int Length(string field) => Throw<int>();

	/// <summary>Returns a substring. Translates to SUBSTRING(field, start).</summary>
	public static string Substring(string field, int start) => Throw<string>();

	/// <summary>Returns a substring. Translates to SUBSTRING(field, start, length).</summary>
	public static string Substring(string field, int start, int length) => Throw<string>();

	/// <summary>Trims whitespace from both sides. Translates to TRIM(field).</summary>
	public static string Trim(string field) => Throw<string>();

	/// <summary>Trims leading whitespace. Translates to LTRIM(field).</summary>
	public static string Ltrim(string field) => Throw<string>();

	/// <summary>Trims trailing whitespace. Translates to RTRIM(field).</summary>
	public static string Rtrim(string field) => Throw<string>();

	/// <summary>Converts to lowercase. Translates to TO_LOWER(field).</summary>
	public static string ToLower(string field) => Throw<string>();

	/// <summary>Converts to uppercase. Translates to TO_UPPER(field).</summary>
	public static string ToUpper(string field) => Throw<string>();

	/// <summary>Concatenates strings. Translates to CONCAT(a, b, ...).</summary>
	public static string Concat(params string[] values) => Throw<string>();

	/// <summary>Replaces occurrences of a substring. Translates to REPLACE(s, old, new).</summary>
	public static string Replace(string field, string oldValue, string newValue) => Throw<string>();

	/// <summary>Finds the position of a substring. Translates to LOCATE(s, substr).</summary>
	public static int Locate(string field, string substring) => Throw<int>();

	/// <summary>Finds the position of a substring starting from a position. Translates to LOCATE(s, substr, start).</summary>
	public static int Locate(string field, string substring, int start) => Throw<int>();

	/// <summary>Returns the leftmost n characters. Translates to LEFT(s, n).</summary>
	public static string Left(string field, int length) => Throw<string>();

	/// <summary>Returns the rightmost n characters. Translates to RIGHT(s, n).</summary>
	public static string Right(string field, int length) => Throw<string>();

	/// <summary>Reverses a string. Translates to REVERSE(s).</summary>
	public static string Reverse(string field) => Throw<string>();

	/// <summary>Repeats a string n times. Translates to REPEAT(s, n).</summary>
	public static string Repeat(string field, int count) => Throw<string>();

	/// <summary>Returns a string of n spaces. Translates to SPACE(n).</summary>
	public static string Space(int count) => Throw<string>();

	/// <summary>Splits a string by delimiter. Translates to SPLIT(s, delim).</summary>
	public static string[] Split(string field, string delimiter) => Throw<string[]>();

	/// <summary>Returns the bit length of a string. Translates to BIT_LENGTH(s).</summary>
	public static int BitLength(string field) => Throw<int>();

	/// <summary>Returns the byte length of a string. Translates to BYTE_LENGTH(s).</summary>
	public static int ByteLength(string field) => Throw<int>();

	/// <summary>Chunks a string into pieces. Translates to CHUNK(s, size).</summary>
	public static string[] Chunk(string field, int size) => Throw<string[]>();

	/// <summary>Decodes a base64 string. Translates to FROM_BASE64(s).</summary>
	public static string FromBase64(string field) => Throw<string>();

	/// <summary>Encodes a string to base64. Translates to TO_BASE64(s).</summary>
	public static string ToBase64(string field) => Throw<string>();

	/// <summary>Hashes a string with the specified algorithm. Translates to HASH(algo, s).</summary>
	public static string Hash(string algorithm, string input) => Throw<string>();

	/// <summary>Computes MD5 hash. Translates to MD5(s).</summary>
	public static string Md5(string field) => Throw<string>();

	/// <summary>Computes SHA1 hash. Translates to SHA1(s).</summary>
	public static string Sha1(string field) => Throw<string>();

	/// <summary>Computes SHA256 hash. Translates to SHA256(s).</summary>
	public static string Sha256(string field) => Throw<string>();

	/// <summary>URL-encodes a string. Translates to URL_ENCODE(s).</summary>
	public static string UrlEncode(string field) => Throw<string>();

	/// <summary>URL-encodes a string component. Translates to URL_ENCODE_COMPONENT(s).</summary>
	public static string UrlEncodeComponent(string field) => Throw<string>();

	/// <summary>URL-decodes a string. Translates to URL_DECODE(s).</summary>
	public static string UrlDecode(string field) => Throw<string>();

	// ============================================================================
	// Pattern Matching
	// ============================================================================

	/// <summary>Performs a full-text match. Translates to MATCH(field, query).</summary>
	public static bool Match(string field, string query) => Throw<bool>();

	/// <summary>Performs a LIKE pattern match. Translates to field LIKE pattern.</summary>
	public static bool Like(string field, string pattern) => Throw<bool>();

	/// <summary>Performs a regex pattern match. Translates to field RLIKE pattern.</summary>
	public static bool Rlike(string field, string pattern) => Throw<bool>();

	// ============================================================================
	// Null Handling
	// ============================================================================

	/// <summary>Checks if a value is null. Translates to field IS NULL.</summary>
	public static bool IsNull<T>(T field) => Throw<bool>();

	/// <summary>Checks if a value is not null. Translates to field IS NOT NULL.</summary>
	public static bool IsNotNull<T>(T field) => Throw<bool>();

	/// <summary>Returns the first non-null value. Translates to COALESCE(a, b, ...).</summary>
	public static T Coalesce<T>(params T[] values) => Throw<T>();

	// ============================================================================
	// Math Functions
	// ============================================================================

	/// <summary>Returns the absolute value. Translates to ABS(n).</summary>
	public static double Abs(double field) => Throw<double>();

	/// <summary>Returns the ceiling. Translates to CEIL(n).</summary>
	public static double Ceil(double field) => Throw<double>();

	/// <summary>Returns the floor. Translates to FLOOR(n).</summary>
	public static double Floor(double field) => Throw<double>();

	/// <summary>Rounds a number. Translates to ROUND(n).</summary>
	public static double Round(double field) => Throw<double>();

	/// <summary>Rounds a number to decimals. Translates to ROUND(n, decimals).</summary>
	public static double Round(double field, int decimals) => Throw<double>();

	/// <summary>Returns the arccosine. Translates to ACOS(n).</summary>
	public static double Acos(double n) => Throw<double>();

	/// <summary>Returns the arcsine. Translates to ASIN(n).</summary>
	public static double Asin(double n) => Throw<double>();

	/// <summary>Returns the arctangent. Translates to ATAN(n).</summary>
	public static double Atan(double n) => Throw<double>();

	/// <summary>Returns the two-argument arctangent. Translates to ATAN2(y, x).</summary>
	public static double Atan2(double y, double x) => Throw<double>();

	/// <summary>Returns the cube root. Translates to CBRT(n).</summary>
	public static double Cbrt(double n) => Throw<double>();

	/// <summary>Returns the cosine. Translates to COS(n).</summary>
	public static double Cos(double n) => Throw<double>();

	/// <summary>Returns the hyperbolic cosine. Translates to COSH(n).</summary>
	public static double Cosh(double n) => Throw<double>();

	/// <summary>Returns the sine. Translates to SIN(n).</summary>
	public static double Sin(double n) => Throw<double>();

	/// <summary>Returns the hyperbolic sine. Translates to SINH(n).</summary>
	public static double Sinh(double n) => Throw<double>();

	/// <summary>Returns the tangent. Translates to TAN(n).</summary>
	public static double Tan(double n) => Throw<double>();

	/// <summary>Returns the hyperbolic tangent. Translates to TANH(n).</summary>
	public static double Tanh(double n) => Throw<double>();

	/// <summary>Returns e raised to a power. Translates to EXP(n).</summary>
	public static double Exp(double n) => Throw<double>();

	/// <summary>Returns the hypotenuse. Translates to HYPOT(a, b).</summary>
	public static double Hypot(double a, double b) => Throw<double>();

	/// <summary>Returns the sign of a number. Translates to SIGNUM(n).</summary>
	public static double Signum(double n) => Throw<double>();

	/// <summary>Copies the sign of one number to another. Translates to COPY_SIGN(magnitude, sign).</summary>
	public static double CopySign(double magnitude, double sign) => Throw<double>();

	/// <summary>Scales a floating-point number by a power of two. Translates to SCALB(n, exp).</summary>
	public static double ScaleB(double n, int exp) => Throw<double>();

	/// <summary>Rounds to a specified number of decimal places. Translates to ROUND_TO(n, dp).</summary>
	public static double RoundTo(double n, int decimalPlaces) => Throw<double>();

	/// <summary>Returns Euler's number. Translates to E().</summary>
	public static double E() => Throw<double>();

	/// <summary>Returns pi. Translates to PI().</summary>
	public static double Pi() => Throw<double>();

	/// <summary>Returns tau (2*pi). Translates to TAU().</summary>
	public static double Tau() => Throw<double>();

	// ============================================================================
	// Conditional Functions
	// ============================================================================

	/// <summary>Clamps a value between min and max. Translates to CLAMP(n, min, max).</summary>
	public static double Clamp(double n, double min, double max) => Throw<double>();

	// ============================================================================
	// Search Functions
	// ============================================================================

	/// <summary>Performs a phrase match. Translates to MATCH_PHRASE(field, phrase).</summary>
	public static bool MatchPhrase(string field, string phrase) => Throw<bool>();

	/// <summary>Performs a KQL query. Translates to KQL(query).</summary>
	public static bool Kql(string query) => Throw<bool>();

	/// <summary>Performs a query string query. Translates to QSTR(query).</summary>
	public static bool Qstr(string query) => Throw<bool>();

	/// <summary>Returns the relevance score. Translates to SCORE().</summary>
	public static double Score() => Throw<double>();

	/// <summary>Applies a decay function. Translates to DECAY(function, field, origin, scale, ...).</summary>
	public static double Decay(string function, string field, string origin, string scale) => Throw<double>();

	/// <summary>Returns top snippets for a field. Translates to TOP_SNIPPETS(field, n).</summary>
	public static string TopSnippets(string field, int count) => Throw<string>();

	// ============================================================================
	// IP Functions
	// ============================================================================

	/// <summary>Checks if an IP matches a CIDR range. Translates to CIDR_MATCH(ip, cidr).</summary>
	public static bool CidrMatch(string ip, string cidr) => Throw<bool>();

	/// <summary>Returns the IP prefix. Translates to IP_PREFIX(ip, prefixLength, ipVersion).</summary>
	public static string IpPrefix(string ip, int prefixLength, int ipVersion) => Throw<string>();

	// ============================================================================
	// Cast Operators (:: syntax)
	// ============================================================================

	/// <summary>Casts to integer. Translates to field::integer.</summary>
	public static int CastToInteger<T>(T field) => Throw<int>();

	/// <summary>Casts to long. Translates to field::long.</summary>
	public static long CastToLong<T>(T field) => Throw<long>();

	/// <summary>Casts to double. Translates to field::double.</summary>
	public static double CastToDouble<T>(T field) => Throw<double>();

	/// <summary>Casts to boolean. Translates to field::boolean.</summary>
	public static bool CastToBoolean<T>(T field) => Throw<bool>();

	/// <summary>Casts to keyword. Translates to field::keyword.</summary>
	public static string CastToKeyword<T>(T field) => Throw<string>();

	/// <summary>Casts to datetime. Translates to field::datetime.</summary>
	public static DateTime CastToDatetime<T>(T field) => Throw<DateTime>();

	/// <summary>Casts to IP. Translates to field::ip.</summary>
	public static string CastToIp<T>(T field) => Throw<string>();

	// ============================================================================
	// Grouping Functions (used in GroupBy key selectors)
	// ============================================================================

	/// <summary>Buckets values into groups. Translates to BUCKET(field, buckets).</summary>
	public static T Bucket<T>(T field, int buckets) => Throw<T>();

	/// <summary>Buckets values using a span expression. Translates to BUCKET(field, span).</summary>
	public static T Bucket<T>(T field, string span) => Throw<T>();

	/// <summary>Time-based bucketing. Translates to TBUCKET(field, span).</summary>
	public static DateTime TBucket(DateTime field, string span) => Throw<DateTime>();

	/// <summary>Categorizes text values. Translates to CATEGORIZE(field).</summary>
	public static string Categorize(string field) => Throw<string>();

	// ============================================================================
	// Aggregation Functions (used in GroupBy result selectors)
	// ============================================================================

	/// <summary>Count distinct values. Translates to COUNT_DISTINCT(field).</summary>
	public static int CountDistinct<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<int>();

	/// <summary>Median value. Translates to MEDIAN(field).</summary>
	public static double Median<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Throw<double>();

	/// <summary>Median absolute deviation. Translates to MEDIAN_ABSOLUTE_DEVIATION(field).</summary>
	public static double MedianAbsoluteDeviation<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Throw<double>();

	/// <summary>Percentile value. Translates to PERCENTILE(field, pct).</summary>
	public static double Percentile<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector, double percentile) => Throw<double>();

	/// <summary>Standard deviation. Translates to STD_DEV(field).</summary>
	public static double StdDev<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Throw<double>();

	/// <summary>Variance. Translates to VARIANCE(field).</summary>
	public static double Variance<TSource>(IEnumerable<TSource> source, Func<TSource, double> selector) => Throw<double>();

	/// <summary>Weighted average. Translates to WEIGHTED_AVG(field, weight).</summary>
	public static double WeightedAvg<TSource>(IEnumerable<TSource> source, Func<TSource, double> valueSelector, Func<TSource, double> weightSelector) => Throw<double>();

	/// <summary>Top N values. Translates to TOP(field, n, order).</summary>
	public static TField[] Top<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector, int count, string order) => Throw<TField[]>();

	/// <summary>All distinct values. Translates to VALUES(field).</summary>
	public static TField[] Values<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<TField[]>();

	/// <summary>First value by sort order. Translates to FIRST(field, sort).</summary>
	public static TField First<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<TField>();

	/// <summary>Last value by sort order. Translates to LAST(field, sort).</summary>
	public static TField Last<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<TField>();

	/// <summary>Random sample. Translates to SAMPLE(field).</summary>
	public static TField Sample<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<TField>();

	/// <summary>True if field is absent from all rows. Translates to ABSENT(field).</summary>
	public static bool Absent<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<bool>();

	/// <summary>True if field is present in any row. Translates to PRESENT(field).</summary>
	public static bool Present<TSource, TField>(IEnumerable<TSource> source, Func<TSource, TField> selector) => Throw<bool>();
}
