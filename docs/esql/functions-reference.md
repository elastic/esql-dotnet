---
navigation_title: Functions reference
---

# ES|QL functions and operators reference

Complete mapping of [ES|QL functions and operators](elasticsearch://reference/query-languages/esql/esql-functions-operators.md) to their Elastic.Esql equivalents. Functions not yet supported are listed at the bottom of each section.

## Aggregation functions

Aggregations run inside `.GroupBy(...).Select(...)` or as terminal operators like `.Count()`.
See [STATS...BY aggregation](linq-translation.md##stats...by-aggregation) for details on the GroupBy pattern.

```csharp
var topLevels = client.Query<LogEntry>()
    .GroupBy(l => l.Level)
    .Select(g => new {
        Level = g.Key,
        Count = g.Count(),
        Avg = g.Average(l => l.Duration),
        P99 = EsqlFunctions.Percentile(g, l => l.Duration, 99)
    });
// STATS count = COUNT(*), avg = AVG(duration), p99 = PERCENTILE(duration, 99) BY level = log.level.keyword
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`ABSENT`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-absent) | `EsqlFunctions.Absent(g, x => x.Field)` | |
| [`AVG`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-avg) | | `g.Average(x => x.Field)` |
| [`COUNT`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-count) | | `g.Count()` or `.Count()` |
| [`COUNT_DISTINCT`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-count_distinct) | `EsqlFunctions.CountDistinct(g, x => x.Field)` | |
| [`FIRST`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-first) | `EsqlFunctions.First(g, x => x.Field)` | |
| [`LAST`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-last) | `EsqlFunctions.Last(g, x => x.Field)` | |
| [`MAX`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-max) | | `g.Max(x => x.Field)` |
| [`MEDIAN`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-median) | `EsqlFunctions.Median(g, x => x.Field)` | |
| [`MEDIAN_ABSOLUTE_DEVIATION`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-median_absolute_deviation) | `EsqlFunctions.MedianAbsoluteDeviation(g, x => x.Field)` | |
| [`MIN`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-min) | | `g.Min(x => x.Field)` |
| [`PERCENTILE`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-percentile) | `EsqlFunctions.Percentile(g, x => x.Field, 99)` | |
| [`PRESENT`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-present) | `EsqlFunctions.Present(g, x => x.Field)` | |
| [`SAMPLE`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-sample) | `EsqlFunctions.Sample(g, x => x.Field)` | |
| [`STD_DEV`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-std_dev) | `EsqlFunctions.StdDev(g, x => x.Field)` | |
| [`SUM`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-sum) | | `g.Sum(x => x.Field)` |
| [`TOP`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-top) | `EsqlFunctions.Top(g, x => x.Field, n, "asc")` | |
| [`VALUES`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-values) | `EsqlFunctions.Values(g, x => x.Field)` | |
| [`VARIANCE`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-variance) | `EsqlFunctions.Variance(g, x => x.Field)` | |
| [`WEIGHTED_AVG`](elasticsearch://reference/query-languages/esql/functions-operators/aggregation-functions.md#esql-weighted_avg) | `EsqlFunctions.WeightedAvg(g, x => x.Val, x => x.Weight)` | |

Not yet supported: `ST_CENTROID_AGG`, `ST_EXTENT_AGG`.

## Conditional functions

Conditional logic in projections. The ternary operator maps to `CASE WHEN`.

```csharp
.Select(l => new { Status = l.StatusCode >= 500 ? "error" : "ok" })
// EVAL status = CASE WHEN statusCode >= 500 THEN "error" ELSE "ok" END
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`CASE`](elasticsearch://reference/query-languages/esql/functions-operators/conditional-functions-and-expressions.md#esql-case) | | `condition ? trueVal : falseVal` |
| [`CLAMP`](elasticsearch://reference/query-languages/esql/functions-operators/conditional-functions-and-expressions.md#esql-clamp) | `EsqlFunctions.Clamp(n, min, max)` | `Math.Clamp(n, min, max)` |
| [`COALESCE`](elasticsearch://reference/query-languages/esql/functions-operators/conditional-functions-and-expressions.md#esql-coalesce) | `EsqlFunctions.Coalesce(a, b)` | |
| [`GREATEST`](elasticsearch://reference/query-languages/esql/functions-operators/conditional-functions-and-expressions.md#esql-greatest) | | `Math.Max(a, b)` |
| [`LEAST`](elasticsearch://reference/query-languages/esql/functions-operators/conditional-functions-and-expressions.md#esql-least) | | `Math.Min(a, b)` |

## Date and time functions

DateTime properties translate to `DATE_EXTRACT`. Arithmetic methods like `.AddDays()` produce date math expressions.

```csharp
.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1) && l.Timestamp.Year == 2025)
// WHERE (@timestamp > (NOW() + -1 hours) AND DATE_EXTRACT("year", @timestamp) == 2025)
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`DATE_DIFF`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-date_diff) | `EsqlFunctions.DateDiff(unit, start, end)` | |
| [`DATE_EXTRACT`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-date_extract) | | `.Year`, `.Month`, `.Day`, `.Hour`, `.Minute`, `.Second`, `.DayOfWeek`, `.DayOfYear` |
| [`DATE_FORMAT`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-date_format) | `EsqlFunctions.DateFormat(field, pattern)` | |
| [`DATE_PARSE`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-date_parse) | `EsqlFunctions.DateParse(pattern, str)` | |
| [`DATE_TRUNC`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-date_trunc) | `EsqlFunctions.DateTrunc(unit, field)` | `DateTime.Today` |
| [`DAY_NAME`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-day_name) | `EsqlFunctions.DayName(date)` | |
| [`MONTH_NAME`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-month_name) | `EsqlFunctions.MonthName(date)` | |
| [`NOW`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-now) | `EsqlFunctions.Now()` | `DateTime.Now`, `DateTime.UtcNow` |
| [`TRANGE`](elasticsearch://reference/query-languages/esql/functions-operators/date-time-functions.md#esql-trange) | `EsqlFunctions.TRange(start, end)` | |
| Date arithmetic | | `.AddDays(n)`, `.AddHours(n)`, `.AddMinutes(n)`, `.AddSeconds(n)`, `.AddMilliseconds(n)` |
| Time intervals | | `TimeSpan.FromDays(n)`, `.FromHours(n)`, `.FromMinutes(n)`, `.FromSeconds(n)` |

## Grouping functions

Grouping uses standard LINQ `.GroupBy()`. ES|QL-specific grouping functions are available through `EsqlFunctions`.
See [STATS...BY aggregation](linq-translation.md##stats...by-aggregation) for the full GroupBy pattern.

```csharp
.GroupBy(l => EsqlFunctions.Bucket(l.Duration, 10))
.Select(g => new { Bucket = g.Key, Count = g.Count() })
// STATS count = COUNT(*) BY bucket = BUCKET(duration, 10)
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`BUCKET`](elasticsearch://reference/query-languages/esql/functions-operators/grouping-functions.md#esql-bucket) | `EsqlFunctions.Bucket(field, n)` or `EsqlFunctions.Bucket(field, span)` | |
| [`CATEGORIZE`](elasticsearch://reference/query-languages/esql/functions-operators/grouping-functions.md#esql-categorize) | `EsqlFunctions.Categorize(field)` | |
| [`TBUCKET`](elasticsearch://reference/query-languages/esql/functions-operators/grouping-functions.md#esql-tbucket) | `EsqlFunctions.TBucket(field, span)` | |

## IP functions

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;
.Where(l => CidrMatch(l.ClientIp, "10.0.0.0/8"))
// WHERE CIDR_MATCH(client_ip, "10.0.0.0/8")
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`CIDR_MATCH`](elasticsearch://reference/query-languages/esql/functions-operators/ip-functions.md#esql-cidr_match) | `EsqlFunctions.CidrMatch(ip, cidr)` | |
| [`IP_PREFIX`](elasticsearch://reference/query-languages/esql/functions-operators/ip-functions.md#esql-ip_prefix) | `EsqlFunctions.IpPrefix(ip, prefixLen, ipVer)` | |

## Math functions

Standard `Math.*` methods translate to their ES|QL equivalents in both Where and Select. `EsqlFunctions` methods also work in both contexts.

```csharp
.Select(l => new { Abs = Math.Abs(l.Delta), Root = Math.Sqrt(l.Value) })
// EVAL abs = ABS(delta), root = SQRT(value)
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`ABS`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-abs) | `EsqlFunctions.Abs(n)` | `Math.Abs(n)` |
| [`ACOS`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-acos) | `EsqlFunctions.Acos(n)` | `Math.Acos(n)` |
| [`ASIN`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-asin) | `EsqlFunctions.Asin(n)` | `Math.Asin(n)` |
| [`ATAN`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-atan) | `EsqlFunctions.Atan(n)` | `Math.Atan(n)` |
| [`ATAN2`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-atan2) | `EsqlFunctions.Atan2(y, x)` | `Math.Atan2(y, x)` |
| [`CBRT`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-cbrt) | `EsqlFunctions.Cbrt(n)` | `Math.Cbrt(n)` |
| [`CEIL`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-ceil) | `EsqlFunctions.Ceil(n)` | `Math.Ceiling(n)` |
| [`COPY_SIGN`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-copy_sign) | `EsqlFunctions.CopySign(mag, sign)` | `Math.CopySign(mag, sign)` |
| [`COS`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-cos) | `EsqlFunctions.Cos(n)` | `Math.Cos(n)` |
| [`COSH`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-cosh) | `EsqlFunctions.Cosh(n)` | `Math.Cosh(n)` |
| [`E`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-e) | `EsqlFunctions.E()` | |
| [`EXP`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-exp) | `EsqlFunctions.Exp(n)` | `Math.Exp(n)` |
| [`FLOOR`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-floor) | `EsqlFunctions.Floor(n)` | `Math.Floor(n)` |
| [`HYPOT`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-hypot) | `EsqlFunctions.Hypot(a, b)` | |
| [`LOG`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-log) | | `Math.Log(n)` |
| [`LOG10`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-log10) | | `Math.Log10(n)` |
| [`PI`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-pi) | `EsqlFunctions.Pi()` | |
| [`POW`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-pow) | | `Math.Pow(base, exp)` |
| [`ROUND`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-round) | `EsqlFunctions.Round(n, decimals)` | `Math.Round(n)` |
| [`ROUND_TO`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-round_to) | `EsqlFunctions.RoundTo(n, dp)` | |
| [`SCALB`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-scalb) | `EsqlFunctions.ScaleB(n, exp)` | `Math.ScaleB(n, exp)` |
| [`SIGNUM`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-signum) | `EsqlFunctions.Signum(n)` | `Math.Sign(n)` |
| [`SIN`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-sin) | `EsqlFunctions.Sin(n)` | `Math.Sin(n)` |
| [`SINH`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-sinh) | `EsqlFunctions.Sinh(n)` | `Math.Sinh(n)` |
| [`SQRT`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-sqrt) | | `Math.Sqrt(n)` |
| [`TAN`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-tan) | `EsqlFunctions.Tan(n)` | `Math.Tan(n)` |
| [`TANH`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-tanh) | `EsqlFunctions.Tanh(n)` | `Math.Tanh(n)` |
| [`TAU`](elasticsearch://reference/query-languages/esql/functions-operators/math-functions.md#esql-tau) | `EsqlFunctions.Tau()` | |

Note: `Math.E`, `Math.PI`, and `Math.Tau` are const fields that the C# compiler inlines as numeric literals. Use `EsqlFunctions.E()`, `.Pi()`, `.Tau()` instead to generate the ES|QL function calls.

## Search functions

Full-text search and pattern matching functions available through `EsqlFunctions`.

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;
.Where(l => Match(l.Message, "connection error"))
// WHERE MATCH(message, "connection error")
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`DECAY`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-decay) | `EsqlFunctions.Decay(func, field, origin, scale)` | |
| [`KQL`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-kql) | `EsqlFunctions.Kql(query)` | |
| [`MATCH`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-match) | `EsqlFunctions.Match(field, query)` | |
| [`MATCH_PHRASE`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-match_phrase) | `EsqlFunctions.MatchPhrase(field, phrase)` | |
| [`QSTR`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-qstr) | `EsqlFunctions.Qstr(query)` | |
| [`SCORE`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-score) | `EsqlFunctions.Score()` | |
| [`TOP_SNIPPETS`](elasticsearch://reference/query-languages/esql/functions-operators/search-functions.md#esql-top_snippets) | `EsqlFunctions.TopSnippets(field, n)` | |

## String functions

C# string methods translate to ES|QL string functions. `Contains`, `StartsWith`, and `EndsWith` map to `LIKE` patterns.

```csharp
.Where(l => l.Host.StartsWith("prod-") && l.Message.ToLower().Contains("timeout"))
// WHERE host LIKE "prod-*" AND TO_LOWER(message) LIKE "*timeout*"
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`BIT_LENGTH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-bit_length) | `EsqlFunctions.BitLength(s)` | |
| [`BYTE_LENGTH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-byte_length) | `EsqlFunctions.ByteLength(s)` | |
| [`CHUNK`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-chunk) | `EsqlFunctions.Chunk(s, size)` | |
| [`CONCAT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-concat) | `EsqlFunctions.Concat(a, b)` | |
| [`ENDS_WITH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-ends_with) | | `s.EndsWith("suffix")` (via LIKE) |
| [`FROM_BASE64`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-from_base64) | `EsqlFunctions.FromBase64(s)` | |
| [`HASH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-hash) | `EsqlFunctions.Hash(algo, s)` | |
| [`LEFT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-left) | `EsqlFunctions.Left(s, n)` | |
| [`LENGTH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-length) | `EsqlFunctions.Length(s)` | `s.Length` |
| [`LOCATE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-locate) | `EsqlFunctions.Locate(s, substr)` | `s.IndexOf(substr)` |
| [`LTRIM`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-ltrim) | `EsqlFunctions.Ltrim(s)` | `s.TrimStart()` |
| [`MD5`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-md5) | `EsqlFunctions.Md5(s)` | |
| [`REPEAT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-repeat) | `EsqlFunctions.Repeat(s, n)` | |
| [`REPLACE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-replace) | `EsqlFunctions.Replace(s, old, new)` | `s.Replace(old, new)` |
| [`REVERSE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-reverse) | `EsqlFunctions.Reverse(s)` | |
| [`RIGHT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-right) | `EsqlFunctions.Right(s, n)` | |
| [`RTRIM`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-rtrim) | `EsqlFunctions.Rtrim(s)` | `s.TrimEnd()` |
| [`SHA1`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-sha1) | `EsqlFunctions.Sha1(s)` | |
| [`SHA256`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-sha256) | `EsqlFunctions.Sha256(s)` | |
| [`SPACE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-space) | `EsqlFunctions.Space(n)` | |
| [`SPLIT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-split) | `EsqlFunctions.Split(s, delim)` | `s.Split(delim)` |
| [`STARTS_WITH`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-starts_with) | | `s.StartsWith("prefix")` (via LIKE) |
| [`SUBSTRING`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-substring) | `EsqlFunctions.Substring(s, start, len)` | `s.Substring(start, len)` or `s[index]` |
| [`TO_BASE64`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-to_base64) | `EsqlFunctions.ToBase64(s)` | |
| [`TO_LOWER`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-to_lower) | `EsqlFunctions.ToLower(s)` | `s.ToLower()` or `s.ToLowerInvariant()` |
| [`TO_UPPER`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-to_upper) | `EsqlFunctions.ToUpper(s)` | `s.ToUpper()` or `s.ToUpperInvariant()` |
| [`TRIM`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-trim) | `EsqlFunctions.Trim(s)` | `s.Trim()` |
| [`URL_DECODE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-url_decode) | `EsqlFunctions.UrlDecode(s)` | |
| [`URL_ENCODE`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-url_encode) | `EsqlFunctions.UrlEncode(s)` | |
| [`URL_ENCODE_COMPONENT`](elasticsearch://reference/query-languages/esql/functions-operators/string-functions.md#esql-url_encode_component) | `EsqlFunctions.UrlEncodeComponent(s)` | |
| LIKE pattern | | `s.Contains("text")` |
| Null/empty checks | | `string.IsNullOrEmpty(s)`, `string.IsNullOrWhiteSpace(s)` |

## Operators

All comparison, arithmetic, logical, and pattern-matching operators are fully supported.

### Comparison

| ES\|QL | C# |
|---|---|
| `==` | `==` |
| `!=` | `!=` |
| `<` | `<` |
| `<=` | `<=` |
| `>` | `>` |
| `>=` | `>=` |

### Arithmetic

| ES\|QL | C# |
|---|---|
| `+` | `+` |
| `-` | `-` |
| `*` | `*` |
| `/` | `/` |
| `%` | `%` |

### Logical

| ES\|QL | C# |
|---|---|
| `AND` | `&&` |
| `OR` | `\|\|` |
| `NOT` | `!` |

### Pattern matching and membership

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;

.Where(l => Like(l.Path, "/api/v?/users"))     // path LIKE "/api/v?/users"
.Where(l => Rlike(l.Path, "/api/v[0-9]+/.*"))  // path RLIKE "/api/v[0-9]+/.*"
.Where(l => levels.Contains(l.Level))           // log.level.keyword IN ("a", "b")
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| [`LIKE`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-like) | `EsqlFunctions.Like(field, pattern)` | |
| [`RLIKE`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-rlike) | `EsqlFunctions.Rlike(field, pattern)` | |
| [`IN`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-in-operator) | | `list.Contains(field)` |
| [`IS NULL`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-is_null) | `EsqlFunctions.IsNull(field)` | `field == null` |
| [`IS NOT NULL`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-is_not_null) | `EsqlFunctions.IsNotNull(field)` | `field != null` |
| [`MATCH`](elasticsearch://reference/query-languages/esql/functions-operators/operators.md#esql-match-operator) | `EsqlFunctions.Match(field, query)` | |

### Cast operator (`::`)

```csharp
.Select(l => new { IntDuration = EsqlFunctions.CastToInteger(l.Duration) })
// EVAL intDuration = duration::integer
```

| ES\|QL | `EsqlFunctions` | C# native |
|---|---|---|
| `field::integer` | `EsqlFunctions.CastToInteger(field)` | |
| `field::long` | `EsqlFunctions.CastToLong(field)` | |
| `field::double` | `EsqlFunctions.CastToDouble(field)` | |
| `field::boolean` | `EsqlFunctions.CastToBoolean(field)` | |
| `field::keyword` | `EsqlFunctions.CastToKeyword(field)` | |
| `field::datetime` | `EsqlFunctions.CastToDatetime(field)` | |
| `field::ip` | `EsqlFunctions.CastToIp(field)` | |

## Not yet supported categories

The following ES|QL function categories have no Elastic.Esql equivalents yet:

**[Spatial functions](elasticsearch://reference/query-languages/esql/functions-operators/spatial-functions.md)**: `ST_CONTAINS`, `ST_DISTANCE`, `ST_DISJOINT`, `ST_ENVELOPE`, `ST_INTERSECTS`, `ST_NPOINTS`, `ST_SIMPLIFY`, `ST_WITHIN`, `ST_X`, `ST_XMAX`, `ST_XMIN`, `ST_Y`, `ST_YMAX`, `ST_YMIN`, `ST_GEOTILE`, `ST_GEOHEX`, `ST_GEOHASH`.

**[Multivalue functions](elasticsearch://reference/query-languages/esql/functions-operators/mv-functions.md)**: `MV_APPEND`, `MV_AVG`, `MV_CONCAT`, `MV_CONTAINS`, `MV_COUNT`, `MV_DEDUPE`, `MV_FIRST`, `MV_INTERSECTION`, `MV_INTERSECTS`, `MV_LAST`, `MV_MAX`, `MV_MEDIAN`, `MV_MEDIAN_ABSOLUTE_DEVIATION`, `MV_MIN`, `MV_PERCENTILE`, `MV_PSERIES_WEIGHTED_SUM`, `MV_SLICE`, `MV_SORT`, `MV_SUM`, `MV_UNION`, `MV_ZIP`.

**[Type conversion functions](elasticsearch://reference/query-languages/esql/functions-operators/type-conversion-functions.md)**: `TO_BOOLEAN`, `TO_CARTESIANPOINT`, `TO_CARTESIANSHAPE`, `TO_DATEPERIOD`, `TO_DATETIME`, `TO_DATE_NANOS`, `TO_DEGREES`, `TO_DENSE_VECTOR`, `TO_DOUBLE`, `TO_GEOHASH`, `TO_GEOHEX`, `TO_GEOPOINT`, `TO_GEOSHAPE`, `TO_GEOTILE`, `TO_INTEGER`, `TO_IP`, `TO_LONG`, `TO_RADIANS`, `TO_STRING`, `TO_TIMEDURATION`, `TO_UNSIGNED_LONG`, `TO_VERSION`, `TO_AGGREGATE_METRIC_DOUBLE`.

**[Dense vector functions](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions.md)**: `KNN`, `TEXT_EMBEDDING`, `V_COSINE`, `V_DOT_PRODUCT`, `V_HAMMING`, `V_L1_NORM`, `V_L2_NORM`.

**[Time series aggregation functions](elasticsearch://reference/query-languages/esql/functions-operators/time-series-aggregation-functions.md)**: `ABSENT_OVER_TIME`, `AVG_OVER_TIME`, `COUNT_OVER_TIME`, `COUNT_DISTINCT_OVER_TIME`, `DELTA`, `DERIV`, `FIRST_OVER_TIME`, `IDELTA`, `INCREASE`, `IRATE`, `LAST_OVER_TIME`, `MAX_OVER_TIME`, `MIN_OVER_TIME`, `PERCENTILE_OVER_TIME`, `PRESENT_OVER_TIME`, `RATE`, `STDDEV_OVER_TIME`, `VARIANCE_OVER_TIME`, `SUM_OVER_TIME`.
