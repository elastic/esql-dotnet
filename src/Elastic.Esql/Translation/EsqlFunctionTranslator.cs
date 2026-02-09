// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

namespace Elastic.Esql.Translation;

/// <summary>
/// Centralized translation for EsqlFunctions.*, Math.*, and string instance methods.
/// Used by both WhereClauseVisitor and SelectProjectionVisitor to avoid duplication.
/// </summary>
internal static class EsqlFunctionTranslator
{
	/// <summary>Translates an EsqlFunctions.* method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslate(string methodName, Func<Expression, string> translate, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			// Date/Time
			"Now" => "NOW()",
			"DateTrunc" => $"DATE_TRUNC({translate(args[0])}, {translate(args[1])})",
			"DateFormat" => $"DATE_FORMAT({translate(args[0])}, {translate(args[1])})",
			"DateDiff" => $"DATE_DIFF({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			"DateParse" => $"DATE_PARSE({translate(args[0])}, {translate(args[1])})",
			"DayName" => $"DAY_NAME({translate(args[0])})",
			"MonthName" => $"MONTH_NAME({translate(args[0])})",
			"TRange" => $"TRANGE({translate(args[0])}, {translate(args[1])})",

			// String
			"Length" => $"LENGTH({translate(args[0])})",
			"Substring" when args.Count == 2 => $"SUBSTRING({translate(args[0])}, {translate(args[1])})",
			"Substring" when args.Count == 3 => $"SUBSTRING({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			"Trim" => $"TRIM({translate(args[0])})",
			"Ltrim" => $"LTRIM({translate(args[0])})",
			"Rtrim" => $"RTRIM({translate(args[0])})",
			"ToLower" => $"TO_LOWER({translate(args[0])})",
			"ToUpper" => $"TO_UPPER({translate(args[0])})",
			"Replace" => $"REPLACE({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			"Locate" when args.Count == 2 => $"LOCATE({translate(args[0])}, {translate(args[1])})",
			"Locate" when args.Count == 3 => $"LOCATE({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			"Left" => $"LEFT({translate(args[0])}, {translate(args[1])})",
			"Right" => $"RIGHT({translate(args[0])}, {translate(args[1])})",
			"Reverse" => $"REVERSE({translate(args[0])})",
			"Repeat" => $"REPEAT({translate(args[0])}, {translate(args[1])})",
			"Space" => $"SPACE({translate(args[0])})",
			"Split" => $"SPLIT({translate(args[0])}, {translate(args[1])})",
			"BitLength" => $"BIT_LENGTH({translate(args[0])})",
			"ByteLength" => $"BYTE_LENGTH({translate(args[0])})",
			"Chunk" => $"CHUNK({translate(args[0])}, {translate(args[1])})",
			"FromBase64" => $"FROM_BASE64({translate(args[0])})",
			"ToBase64" => $"TO_BASE64({translate(args[0])})",
			"Hash" => $"HASH({translate(args[0])}, {translate(args[1])})",
			"Md5" => $"MD5({translate(args[0])})",
			"Sha1" => $"SHA1({translate(args[0])})",
			"Sha256" => $"SHA256({translate(args[0])})",
			"UrlEncode" => $"URL_ENCODE({translate(args[0])})",
			"UrlEncodeComponent" => $"URL_ENCODE_COMPONENT({translate(args[0])})",
			"UrlDecode" => $"URL_DECODE({translate(args[0])})",

			// Null Handling
			"Coalesce" => TranslateParamsCall("COALESCE", translate, args),
			"IsNull" => $"{translate(args[0])} IS NULL",
			"IsNotNull" => $"{translate(args[0])} IS NOT NULL",

			// Math
			"Abs" => $"ABS({translate(args[0])})",
			"Ceil" => $"CEIL({translate(args[0])})",
			"Floor" => $"FLOOR({translate(args[0])})",
			"Round" when args.Count == 1 => $"ROUND({translate(args[0])})",
			"Round" when args.Count == 2 => $"ROUND({translate(args[0])}, {translate(args[1])})",
			"Acos" => $"ACOS({translate(args[0])})",
			"Asin" => $"ASIN({translate(args[0])})",
			"Atan" => $"ATAN({translate(args[0])})",
			"Atan2" => $"ATAN2({translate(args[0])}, {translate(args[1])})",
			"Cbrt" => $"CBRT({translate(args[0])})",
			"Cos" => $"COS({translate(args[0])})",
			"Cosh" => $"COSH({translate(args[0])})",
			"Sin" => $"SIN({translate(args[0])})",
			"Sinh" => $"SINH({translate(args[0])})",
			"Tan" => $"TAN({translate(args[0])})",
			"Tanh" => $"TANH({translate(args[0])})",
			"Exp" => $"EXP({translate(args[0])})",
			"Hypot" => $"HYPOT({translate(args[0])}, {translate(args[1])})",
			"Signum" => $"SIGNUM({translate(args[0])})",
			"CopySign" => $"COPY_SIGN({translate(args[0])}, {translate(args[1])})",
			"ScaleB" => $"SCALB({translate(args[0])}, {translate(args[1])})",
			"RoundTo" => $"ROUND_TO({translate(args[0])}, {translate(args[1])})",
			"E" when args.Count == 0 => "E()",
			"Pi" when args.Count == 0 => "PI()",
			"Tau" when args.Count == 0 => "TAU()",
			"Clamp" => $"CLAMP({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",

			// Pattern Matching
			"Match" => $"MATCH({translate(args[0])}, {translate(args[1])})",
			"Like" => $"{translate(args[0])} LIKE {translate(args[1])}",
			"Rlike" => $"{translate(args[0])} RLIKE {translate(args[1])}",

			// Search
			"MatchPhrase" => $"MATCH_PHRASE({translate(args[0])}, {translate(args[1])})",
			"Kql" => $"KQL({translate(args[0])})",
			"Qstr" => $"QSTR({translate(args[0])})",
			"Score" when args.Count == 0 => "SCORE()",
			"Decay" => $"DECAY({string.Join(", ", args.Select(translate))})",
			"TopSnippets" => $"TOP_SNIPPETS({translate(args[0])}, {translate(args[1])})",

			// IP
			"CidrMatch" => $"CIDR_MATCH({translate(args[0])}, {translate(args[1])})",
			"IpPrefix" => $"IP_PREFIX({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",

			// Cast
			"CastToInteger" => $"{translate(args[0])}::integer",
			"CastToLong" => $"{translate(args[0])}::long",
			"CastToDouble" => $"{translate(args[0])}::double",
			"CastToBoolean" => $"{translate(args[0])}::boolean",
			"CastToKeyword" => $"{translate(args[0])}::keyword",
			"CastToDatetime" => $"{translate(args[0])}::datetime",
			"CastToIp" => $"{translate(args[0])}::ip",

			// Concat (params)
			"Concat" => TranslateParamsCall("CONCAT", translate, args),

			// Grouping (used in GroupBy key selectors, but also translatable as expressions)
			"Bucket" => args.Count == 2 ? $"BUCKET({translate(args[0])}, {translate(args[1])})" : null,
			"TBucket" => $"TBUCKET({translate(args[0])}, {translate(args[1])})",
			"Categorize" => $"CATEGORIZE({translate(args[0])})",

			_ => null
		};

	/// <summary>Translates a Math.* method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateMath(string methodName, Func<Expression, string> translate, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			"Abs" => $"ABS({translate(args[0])})",
			"Ceiling" => $"CEIL({translate(args[0])})",
			"Floor" => $"FLOOR({translate(args[0])})",
			"Round" when args.Count == 1 => $"ROUND({translate(args[0])})",
			"Round" when args.Count == 2 => $"ROUND({translate(args[0])}, {translate(args[1])})",
			"Max" => $"GREATEST({translate(args[0])}, {translate(args[1])})",
			"Min" => $"LEAST({translate(args[0])}, {translate(args[1])})",
			"Pow" => $"POW({translate(args[0])}, {translate(args[1])})",
			"Sqrt" => $"SQRT({translate(args[0])})",
			"Log" when args.Count == 1 => $"LOG({translate(args[0])})",
			"Log10" => $"LOG10({translate(args[0])})",
			"Acos" => $"ACOS({translate(args[0])})",
			"Asin" => $"ASIN({translate(args[0])})",
			"Atan" => $"ATAN({translate(args[0])})",
			"Atan2" => $"ATAN2({translate(args[0])}, {translate(args[1])})",
			"Cbrt" => $"CBRT({translate(args[0])})",
			"Cos" => $"COS({translate(args[0])})",
			"Cosh" => $"COSH({translate(args[0])})",
			"Sin" => $"SIN({translate(args[0])})",
			"Sinh" => $"SINH({translate(args[0])})",
			"Tan" => $"TAN({translate(args[0])})",
			"Tanh" => $"TANH({translate(args[0])})",
			"Exp" => $"EXP({translate(args[0])})",
			"Sign" => $"SIGNUM({translate(args[0])})",
			"CopySign" => $"COPY_SIGN({translate(args[0])}, {translate(args[1])})",
			"ScaleB" => $"SCALB({translate(args[0])}, {translate(args[1])})",
			"Clamp" => $"CLAMP({translate(args[0])}, {translate(args[1])}, {translate(args[2])})",
			_ => null
		};

	/// <summary>Translates a string instance method call to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateString(string methodName, Func<Expression, string> translate, string target, IReadOnlyList<Expression> args) =>
		methodName switch
		{
			"ToLower" or "ToLowerInvariant" => $"TO_LOWER({target})",
			"ToUpper" or "ToUpperInvariant" => $"TO_UPPER({target})",
			"Trim" => $"TRIM({target})",
			"TrimStart" => $"LTRIM({target})",
			"TrimEnd" => $"RTRIM({target})",
			"Substring" when args.Count == 1 => $"SUBSTRING({target}, {translate(args[0])})",
			"Substring" when args.Count == 2 => $"SUBSTRING({target}, {translate(args[0])}, {translate(args[1])})",
			"Replace" => $"REPLACE({target}, {translate(args[0])}, {translate(args[1])})",
			"IndexOf" when args.Count == 1 => $"LOCATE({target}, {translate(args[0])})",
			"IndexOf" when args.Count == 2 => $"LOCATE({target}, {translate(args[0])}, {translate(args[1])})",
			"Split" when args.Count >= 1 => $"SPLIT({target}, {translate(args[0])})",
			_ => null
		};

	/// <summary>Translates a Math static field/const access to ES|QL. Returns null if not recognized.</summary>
	public static string? TryTranslateMathConstant(string memberName) =>
		memberName switch
		{
			"E" => "E()",
			"PI" => "PI()",
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
