// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Elastic.Esql.Functions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Centralized translation for EsqlFunctions.*, Math.*, and string instance methods.
/// Used by both WhereClauseVisitor and SelectProjectionVisitor to avoid duplication.
/// </summary>
internal static class EsqlFunctionTranslator
{
	/// <summary>
	/// Translates a method call based on its declaring type using the centralized
	/// EsqlFunctions/Math/string translators. Returns null when unsupported.
	/// </summary>
	public static string? TryTranslateMethodCall(MethodCallExpression methodCall, Func<Expression, string> translate)
	{
		var methodName = methodCall.Method.Name;
		var declaringType = methodCall.Method.DeclaringType;

		if (declaringType == typeof(EsqlFunctions))
			return TryTranslate(methodName, translate, methodCall.Arguments);

		if (declaringType == typeof(Math))
			return TryTranslateMath(methodName, translate, methodCall.Arguments);

		if (declaringType == typeof(string) && methodCall.Object is not null)
		{
			var target = translate(methodCall.Object);
			return TryTranslateString(methodName, translate, target, methodCall.Arguments);
		}

		return null;
	}

	/// <summary>Translates an EsqlFunctions.* method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslate(string methodName, Func<Expression, string> translate, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			// Date/Time
			nameof(EsqlFunctions.Now) => "NOW()",
			nameof(EsqlFunctions.DateTrunc) => $"DATE_TRUNC({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.DateFormat) => $"DATE_FORMAT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.DateDiff) => $"DATE_DIFF({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			nameof(EsqlFunctions.DateParse) => $"DATE_PARSE({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.DayName) => $"DAY_NAME({translate(args[0])})",
			nameof(EsqlFunctions.MonthName) => $"MONTH_NAME({translate(args[0])})",
			nameof(EsqlFunctions.TRange) => $"TRANGE({translate(args[0])}, {translate(args[1])})",

			// String
			nameof(EsqlFunctions.Length) => $"LENGTH({translate(args[0])})",
			nameof(EsqlFunctions.Substring) when args.Count == 2 => $"SUBSTRING({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Substring) when args.Count == 3 => $"SUBSTRING({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			nameof(EsqlFunctions.Trim) => $"TRIM({translate(args[0])})",
			nameof(EsqlFunctions.Ltrim) => $"LTRIM({translate(args[0])})",
			nameof(EsqlFunctions.Rtrim) => $"RTRIM({translate(args[0])})",
			nameof(EsqlFunctions.ToLower) => $"TO_LOWER({translate(args[0])})",
			nameof(EsqlFunctions.ToUpper) => $"TO_UPPER({translate(args[0])})",
			nameof(EsqlFunctions.Replace) => $"REPLACE({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			nameof(EsqlFunctions.Locate) when args.Count == 2 => $"LOCATE({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Locate) when args.Count == 3 => $"LOCATE({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			nameof(EsqlFunctions.Left) => $"LEFT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Right) => $"RIGHT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Reverse) => $"REVERSE({translate(args[0])})",
			nameof(EsqlFunctions.Repeat) => $"REPEAT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Space) => $"SPACE({translate(args[0])})",
			nameof(EsqlFunctions.Split) => $"SPLIT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.BitLength) => $"BIT_LENGTH({translate(args[0])})",
			nameof(EsqlFunctions.ByteLength) => $"BYTE_LENGTH({translate(args[0])})",
			nameof(EsqlFunctions.Chunk) => $"CHUNK({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.FromBase64) => $"FROM_BASE64({translate(args[0])})",
			nameof(EsqlFunctions.ToBase64) => $"TO_BASE64({translate(args[0])})",
			nameof(EsqlFunctions.Hash) => $"HASH({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Md5) => $"MD5({translate(args[0])})",
			nameof(EsqlFunctions.Sha1) => $"SHA1({translate(args[0])})",
			nameof(EsqlFunctions.Sha256) => $"SHA256({translate(args[0])})",
			nameof(EsqlFunctions.UrlEncode) => $"URL_ENCODE({translate(args[0])})",
			nameof(EsqlFunctions.UrlEncodeComponent) => $"URL_ENCODE_COMPONENT({translate(args[0])})",
			nameof(EsqlFunctions.UrlDecode) => $"URL_DECODE({translate(args[0])})",

			// Null Handling
			nameof(EsqlFunctions.Coalesce) => TranslateParamsCall("COALESCE", translate, args),
			nameof(EsqlFunctions.IsNull) => $"{translate(args[0])} IS NULL",
			nameof(EsqlFunctions.IsNotNull) => $"{translate(args[0])} IS NOT NULL",

			// Math
			nameof(EsqlFunctions.Abs) => $"ABS({translate(args[0])})",
			nameof(EsqlFunctions.Ceil) => $"CEIL({translate(args[0])})",
			nameof(EsqlFunctions.Floor) => $"FLOOR({translate(args[0])})",
			nameof(EsqlFunctions.Round) when args.Count == 1 => $"ROUND({translate(args[0])})",
			nameof(EsqlFunctions.Round) when args.Count == 2 => $"ROUND({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Acos) => $"ACOS({translate(args[0])})",
			nameof(EsqlFunctions.Asin) => $"ASIN({translate(args[0])})",
			nameof(EsqlFunctions.Atan) => $"ATAN({translate(args[0])})",
			nameof(EsqlFunctions.Atan2) => $"ATAN2({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Cbrt) => $"CBRT({translate(args[0])})",
			nameof(EsqlFunctions.Cos) => $"COS({translate(args[0])})",
			nameof(EsqlFunctions.Cosh) => $"COSH({translate(args[0])})",
			nameof(EsqlFunctions.Sin) => $"SIN({translate(args[0])})",
			nameof(EsqlFunctions.Sinh) => $"SINH({translate(args[0])})",
			nameof(EsqlFunctions.Tan) => $"TAN({translate(args[0])})",
			nameof(EsqlFunctions.Tanh) => $"TANH({translate(args[0])})",
			nameof(EsqlFunctions.Exp) => $"EXP({translate(args[0])})",
			nameof(EsqlFunctions.Hypot) => $"HYPOT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Signum) => $"SIGNUM({translate(args[0])})",
			nameof(EsqlFunctions.CopySign) => $"COPY_SIGN({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.ScaleB) => $"SCALB({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.RoundTo) => $"ROUND_TO({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.E) when args.Count == 0 => "E()",
			nameof(EsqlFunctions.Pi) when args.Count == 0 => "PI()",
			nameof(EsqlFunctions.Tau) when args.Count == 0 => "TAU()",
			nameof(EsqlFunctions.Clamp) => $"CLAMP({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",

			// Pattern Matching
			nameof(EsqlFunctions.Match) => $"MATCH({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Like) => $"{translate(args[0])} LIKE {translate(args[1])}",
			nameof(EsqlFunctions.Rlike) => $"{translate(args[0])} RLIKE {translate(args[1])}",

			// Search
			nameof(EsqlFunctions.MatchPhrase) => $"MATCH_PHRASE({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Kql) => $"KQL({translate(args[0])})",
			nameof(EsqlFunctions.Qstr) => $"QSTR({translate(args[0])})",
			nameof(EsqlFunctions.Score) when args.Count == 0 => "SCORE()",
			nameof(EsqlFunctions.Decay) => $"DECAY({string.Join(", ", args.Select(translate))})",
			nameof(EsqlFunctions.TopSnippets) => $"TOP_SNIPPETS({translate(args[0])}, {translate(args[1])})",

			// IP
			nameof(EsqlFunctions.CidrMatch) => $"CIDR_MATCH({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.IpPrefix) => $"IP_PREFIX({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",

			// Cast
			nameof(EsqlFunctions.CastToInteger) => $"{translate(args[0])}::integer",
			nameof(EsqlFunctions.CastToLong) => $"{translate(args[0])}::long",
			nameof(EsqlFunctions.CastToDouble) => $"{translate(args[0])}::double",
			nameof(EsqlFunctions.CastToBoolean) => $"{translate(args[0])}::boolean",
			nameof(EsqlFunctions.CastToKeyword) => $"{translate(args[0])}::keyword",
			nameof(EsqlFunctions.CastToDatetime) => $"{translate(args[0])}::datetime",
			nameof(EsqlFunctions.CastToIp) => $"{translate(args[0])}::ip",

			// Concat (params)
			nameof(EsqlFunctions.Concat) => TranslateParamsCall("CONCAT", translate, args),

			// Grouping (used in GroupBy key selectors, but also translatable as expressions)
			nameof(EsqlFunctions.Bucket) => args.Count == 2 ? $"BUCKET({translate(args[0])}, {translate(args[1])})" : null,
			nameof(EsqlFunctions.TBucket) => $"TBUCKET({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Categorize) => $"CATEGORIZE({translate(args[0])})",

			// Dense vector functions
			nameof(EsqlFunctions.Knn) when args.Count == 2 => $"KNN({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.Knn) when args.Count == 3 => $"KNN({translate(args[0])}, {translate(args[1])}, {TranslateKnnOptions(args[2])})",
			nameof(EsqlFunctions.TextEmbedding) => $"TEXT_EMBEDDING({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.VCosine) => $"V_COSINE({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.VDotProduct) => $"V_DOT_PRODUCT({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.VHamming) => $"V_HAMMING({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.VL1Norm) => $"V_L1_NORM({translate(args[0])}, {translate(args[1])})",
			nameof(EsqlFunctions.VL2Norm) => $"V_L2_NORM({translate(args[0])}, {translate(args[1])})",

			_ => null
		};

	/// <summary>
	/// Renders a <see cref="KnnOptions"/> argument expression as the ES|QL named-parameter object
	/// literal (e.g. <c>{ "k": 10, "num_candidates": 100 }</c>). Only non-null properties are emitted.
	/// </summary>
	private static string TranslateKnnOptions(Expression expression)
	{
		var resolved = ExpressionConstantResolver.Resolve(expression)
			?? throw new NotSupportedException("KnnOptions argument must not be null. Use the two-argument 'Knn' overload to omit options.");

		if (resolved is not KnnOptions options)
			throw new NotSupportedException(
				$"Knn options argument must be a '{nameof(KnnOptions)}' value (got '{resolved.GetType().Name}').");

		var sb = new StringBuilder("{ ");
		var first = true;

		AppendIntOption(sb, ref first, "k", options.K);
		AppendIntOption(sb, ref first, "min_candidates", options.MinCandidates);
		AppendDoubleOption(sb, ref first, "similarity", options.Similarity);
		AppendDoubleOption(sb, ref first, "boost", options.Boost);
		AppendDoubleOption(sb, ref first, "visit_percentage", options.VisitPercentage);
		AppendDoubleOption(sb, ref first, "rescore_oversample", options.RescoreOversample);

		_ = sb.Append(" }");
		return sb.ToString();
	}

	private static void AppendIntOption(StringBuilder sb, ref bool first, string name, int? value)
	{
		if (value is null)
			return;

		if (!first)
			_ = sb.Append(", ");
		first = false;

		_ = sb.Append('"').Append(name).Append("\": ").Append(value.Value.ToString(CultureInfo.InvariantCulture));
	}

	private static void AppendDoubleOption(StringBuilder sb, ref bool first, string name, double? value)
	{
		if (value is null)
			return;

		if (!first)
			_ = sb.Append(", ");
		first = false;

		_ = sb.Append('"').Append(name).Append("\": ").Append(value.Value.ToString("G", CultureInfo.InvariantCulture));
	}

	/// <summary>Translates a Math.* method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateMath(string methodName, Func<Expression, string> translate, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			nameof(Math.Abs) => $"ABS({translate(args[0])})",
			nameof(Math.Ceiling) => $"CEIL({translate(args[0])})",
			nameof(Math.Floor) => $"FLOOR({translate(args[0])})",
			nameof(Math.Round) when args.Count == 1 => $"ROUND({translate(args[0])})",
			nameof(Math.Round) when args.Count == 2 => $"ROUND({translate(args[0])}, {translate(args[1])})",
			nameof(Math.Max) => $"GREATEST({translate(args[0])}, {translate(args[1])})",
			nameof(Math.Min) => $"LEAST({translate(args[0])}, {translate(args[1])})",
			nameof(Math.Pow) => $"POW({translate(args[0])}, {translate(args[1])})",
			nameof(Math.Sqrt) => $"SQRT({translate(args[0])})",
			nameof(Math.Log) when args.Count == 1 => $"LOG({translate(args[0])})",
			nameof(Math.Log10) => $"LOG10({translate(args[0])})",
			nameof(Math.Acos) => $"ACOS({translate(args[0])})",
			nameof(Math.Asin) => $"ASIN({translate(args[0])})",
			nameof(Math.Atan) => $"ATAN({translate(args[0])})",
			nameof(Math.Atan2) => $"ATAN2({translate(args[0])}, {translate(args[1])})",
			"Cbrt" => $"CBRT({translate(args[0])})",
			nameof(Math.Cos) => $"COS({translate(args[0])})",
			nameof(Math.Cosh) => $"COSH({translate(args[0])})",
			nameof(Math.Sin) => $"SIN({translate(args[0])})",
			nameof(Math.Sinh) => $"SINH({translate(args[0])})",
			nameof(Math.Tan) => $"TAN({translate(args[0])})",
			nameof(Math.Tanh) => $"TANH({translate(args[0])})",
			nameof(Math.Exp) => $"EXP({translate(args[0])})",
			nameof(Math.Sign) => $"SIGNUM({translate(args[0])})",
			"CopySign" => $"COPY_SIGN({translate(args[0])}, {translate(args[1])})",
			"ScaleB" => $"SCALB({translate(args[0])}, {translate(args[1])})",
			"Clamp" => $"CLAMP({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			_ => null
		};

	/// <summary>Translates a string instance method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateString(string methodName, Func<Expression, string> translate, string target, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			nameof(string.ToLower) or nameof(string.ToLowerInvariant) => $"TO_LOWER({target})",
			nameof(string.ToUpper) or nameof(string.ToUpperInvariant) => $"TO_UPPER({target})",
			nameof(string.Trim) => $"TRIM({target})",
			nameof(string.TrimStart) => $"LTRIM({target})",
			nameof(string.TrimEnd) => $"RTRIM({target})",
			nameof(string.Substring) when args.Count == 1 => $"SUBSTRING({target}, {translate(args[0])})",
			nameof(string.Substring) when args.Count == 2 => $"SUBSTRING({target}, {translate(args[0])}, {translate(args[1])})",
			nameof(string.Replace) => $"REPLACE({target}, {translate(args[0])}, {translate(args[1])})",
			nameof(string.IndexOf) when args.Count == 1 => $"LOCATE({target}, {translate(args[0])})",
			nameof(string.IndexOf) when args.Count == 2 => $"LOCATE({target}, {translate(args[0])}, {translate(args[1])})",
			nameof(string.Split) when args.Count >= 1 => $"SPLIT({target}, {translate(args[0])})",
			_ => null
		};

	/// <summary>
	/// Translates a DateTime/DateTimeOffset instance property access to DATE_EXTRACT.
	/// ES|QL DATE_EXTRACT accepts java.time.temporal.ChronoField names only; short aliases
	/// like "month" or "hour" are rejected server-side. Returns null if not recognized.
	/// </summary>
	public static string? TryTranslateDateMember(string memberName, string target) =>
		memberName switch
		{
			nameof(DateTime.Year) => $"DATE_EXTRACT(\"year\", {target})",
			nameof(DateTime.Month) => $"DATE_EXTRACT(\"month_of_year\", {target})",
			nameof(DateTime.Day) => $"DATE_EXTRACT(\"day_of_month\", {target})",
			nameof(DateTime.Hour) => $"DATE_EXTRACT(\"hour_of_day\", {target})",
			nameof(DateTime.Minute) => $"DATE_EXTRACT(\"minute_of_hour\", {target})",
			nameof(DateTime.Second) => $"DATE_EXTRACT(\"second_of_minute\", {target})",
			nameof(DateTime.DayOfWeek) => $"DATE_EXTRACT(\"day_of_week\", {target})",
			nameof(DateTime.DayOfYear) => $"DATE_EXTRACT(\"day_of_year\", {target})",
			_ => null
		};

	/// <summary>Translates a Math static field/const access to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateMathConstant(string memberName) =>
		memberName switch
		{
			nameof(Math.E) => "E()",
			nameof(Math.PI) => "PI()",
			"Tau" => "TAU()",
			_ => null
		};

	private static string TranslateParamsCall(string functionName, Func<Expression, string> translate, IReadOnlyList<Expression> args)
	{
		var translated = new List<string>();

		foreach (var arg in args)
		{
			if (arg is NewArrayExpression newArray)
			{
				foreach (var elem in newArray.Expressions)
					translated.Add(translate(elem));
			}
			else
				translated.Add(translate(arg));
		}

		return $"{functionName}({string.Join(", ", translated)})";
	}
}
