---
navigation_title: LINQ translation
---

# LINQ to ES|QL translation

How LINQ operators map to [ES|QL commands](elasticsearch://reference/query-languages/esql.md). Elastic.Esql uses expression visitors to translate LINQ method chains into the ES|QL pipe-delimited command syntax.

## FROM - source resolution

Every query starts with `FROM`. The source index pattern is specified using the `.From()` extension method:

```csharp
var query = new EsqlQueryable<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .ToString();
```

```
FROM logs-*
| WHERE log.level == "ERROR"
```

The `.From()` method accepts any index pattern string:

```csharp
.From("logs-*")           // FROM logs-*
.From("products")         // FROM products
.From("orders-2025.*")    // FROM orders-2025.*
```

If `.From()` is not called, the type name is used as the default index pattern with camelCase convention (e.g., `LogEntry` becomes `FROM logEntry`).

## Field name resolution

Field names are resolved from your C# type using `System.Text.Json` conventions:

- `[JsonPropertyName("@timestamp")]` on a property produces `@timestamp` in the query
- Properties without `[JsonPropertyName]` are resolved using the configured `JsonNamingPolicy` (defaults to camelCase)

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }  // → @timestamp

    [JsonPropertyName("log.level")]
    public string Level { get; set; }        // → log.level

    public string Message { get; set; }      // → message
    public int StatusCode { get; set; }      // → statusCode
}
```

## WHERE - filtering

`.Where()` translates to the ES|QL [`WHERE`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-where) command. Multiple `.Where()` calls produce multiple `WHERE` commands:

```csharp
query
    .Where(l => l.StatusCode >= 500)
    .Where(l => l.Level == "ERROR")
```

```
FROM logs-*
| WHERE statusCode >= 500
| WHERE log.level == "ERROR"
```

### Null handling

```csharp
.Where(l => l.Tag == null)             // WHERE tag IS NULL
.Where(l => l.Tag != null)             // WHERE tag IS NOT NULL
```

### Compound conditions

```csharp
.Where(l => l.Level == "ERROR" && l.Duration > 1000)
// WHERE (log.level == "ERROR" AND duration > 1000)

.Where(l => l.Level == "ERROR" || l.Level == "FATAL")
// WHERE (log.level == "ERROR" OR log.level == "FATAL")

.Where(l => !(l.StatusCode >= 500))
// WHERE NOT (statusCode >= 500)
```

### IN operator

```csharp
var levels = new[] { "ERROR", "FATAL", "CRITICAL" };
query.Where(l => levels.Contains(l.Level))
// WHERE log.level IN ("ERROR", "FATAL", "CRITICAL")
```

### Boolean fields

```csharp
.Where(l => l.IsError)                 // WHERE isError
.Where(l => !l.IsError)                // WHERE NOT isError
```

### String methods

```csharp
.Where(l => l.Message.Contains("timeout"))    // WHERE message LIKE "*timeout*"
.Where(l => l.Host.StartsWith("prod-"))       // WHERE host LIKE "prod-*"
.Where(l => l.Path.EndsWith(".json"))          // WHERE path LIKE "*.json"
.Where(l => string.IsNullOrEmpty(l.Tag))       // WHERE (tag IS NULL OR tag == "")
```

### Captured variables and parameterization

Captured C# variables are inlined by default:

```csharp
var minStatus = 400;
query.Where(l => l.StatusCode >= minStatus)
// WHERE statusCode >= 400
```

Use `.ToEsqlString(inlineParameters: false)` to extract them as named `?param` placeholders:

```csharp
var esql = query
    .Where(l => l.StatusCode >= minStatus)
    .ToEsqlString(inlineParameters: false);
// WHERE statusCode >= ?minStatus
```

The parameter values are retrievable via `.GetParameters()` for passing to the ES|QL API.

## STATS...BY - aggregation

`.GroupBy()` followed by `.Select()` translates to the ES|QL [`STATS...BY`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-stats-by) command:

```csharp
query
    .GroupBy(l => l.Level)
    .Select(g => new
    {
        Level = g.Key,
        Count = g.Count(),
        AvgDuration = g.Average(l => l.Duration)
    })
```

```
FROM logs-*
| STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level
```

### Composite group keys

```csharp
query
    .GroupBy(l => new { l.Level, l.Host })
    .Select(g => new
    {
        Level = g.Key.Level,
        Host = g.Key.Host,
        Count = g.Count()
    })
```

```
FROM logs-*
| STATS count = COUNT(*) BY level = log.level, host
```

### ES|QL grouping functions

Use `EsqlFunctions.Bucket()`, `EsqlFunctions.TBucket()`, and `EsqlFunctions.Categorize()` in group keys:

```csharp
query
    .GroupBy(l => EsqlFunctions.Bucket(l.Duration, 10))
    .Select(g => new { Bucket = g.Key, Count = g.Count() })
// STATS count = COUNT(*) BY bucket = BUCKET(duration, 10)

query
    .GroupBy(l => EsqlFunctions.TBucket(l.Timestamp, "1 hour"))
    .Select(g => new { Hour = g.Key, Count = g.Count() })
// STATS count = COUNT(*) BY hour = TBUCKET(@timestamp, "1 hour")
```

### Terminal aggregation operators

Aggregation methods called directly on the queryable produce `STATS` without `BY`:

```csharp
query.Where(l => l.Level == "ERROR").Count()
// FROM logs-* | WHERE log.level == "ERROR" | STATS count = COUNT(*)

query.Sum(l => l.Duration)
// FROM logs-* | STATS sum = SUM(duration)
```

### Advanced aggregation functions

Beyond standard LINQ aggregations (`Count`, `Sum`, `Average`, `Min`, `Max`), ES|QL-specific aggregations are available through `EsqlFunctions`:

```csharp
.Select(g => new
{
    P99 = EsqlFunctions.Percentile(g, l => l.Duration, 99),
    Med = EsqlFunctions.Median(g, l => l.Duration),
    Distinct = EsqlFunctions.CountDistinct(g, l => l.Host),
    Dev = EsqlFunctions.StdDev(g, l => l.Duration),
    Vals = EsqlFunctions.Values(g, l => l.Host)
})
```

See the [functions reference](functions-reference.md) for the complete list of aggregation functions.

## SORT - ordering

`.OrderBy()` and `.OrderByDescending()` translate to the ES|QL [`SORT`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-sort) command:

```csharp
query
    .OrderBy(l => l.Level)
    .ThenByDescending(l => l.Timestamp)
```

```
FROM logs-*
| SORT log.level, @timestamp DESC
```

## LIMIT - row count

`.Take()` translates to the ES|QL [`LIMIT`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-limit) command:

```csharp
query.Take(100)
// | LIMIT 100
```

`.First()` and `.FirstOrDefault()` produce `LIMIT 1`. `.Single()` and `.SingleOrDefault()` produce `LIMIT 2` (to validate exactly one result).

## KEEP - projection

`.Select()` translates to `KEEP` for simple field selections and `EVAL` for computed fields:

```csharp
// Simple projection → KEEP
query.Select(l => new { l.Message, l.Timestamp })
// | KEEP message, @timestamp

// Computed fields → EVAL + KEEP
query.Select(l => new { l.Message, Secs = l.Duration / 1000 })
// | EVAL secs = (duration / 1000)
// | KEEP message, secs

// Function calls → EVAL
query.Select(l => new { Upper = l.Message.ToUpper(), Hour = l.Timestamp.Hour })
// | EVAL upper = TO_UPPER(message), hour = DATE_EXTRACT("hour", @timestamp)
```

### Conditional projections

The ternary operator translates to `CASE`:

```csharp
query.Select(l => new { Status = l.StatusCode >= 500 ? "error" : "ok" })
// | EVAL status = CASE WHEN statusCode >= 500 THEN "error" ELSE "ok" END
```

### Renamed fields

When a property name in the anonymous type differs from the source field name, a `RENAME` command is generated:

```csharp
query.Select(l => new { Msg = l.Message })
// | RENAME message AS msg
// | KEEP msg
```

## KEEP and DROP extensions

In addition to `.Select()`, explicit `.Keep()` and `.Drop()` extension methods are available for fine-grained control:

### KEEP with string field names

```csharp
query.Keep("message", "statusCode")
// | KEEP message, statusCode
```

### KEEP with lambda selectors

```csharp
query.Keep(l => l.Message, l => l.StatusCode)
// | KEEP message, statusCode
```

Lambda selectors resolve field names from `[JsonPropertyName]` attributes automatically.

### KEEP with projection

```csharp
query.Keep(l => new { l.Message, l.StatusCode })
// | KEEP message, statusCode

query.Keep(l => new { Msg = l.Message })
// | RENAME message AS msg
// | KEEP msg
```

### DROP with string field names

```csharp
query.Drop("duration", "host")
// | DROP duration, host
```

### DROP with lambda selectors

```csharp
query.Drop(l => l.Duration, l => l.Host)
// | DROP duration, host
```

## LOOKUP JOIN - cross-index correlation

ES|QL's `LOOKUP JOIN` command correlates data from a lookup index. Multiple API styles are supported.

### LookupJoin with key selectors

The most explicit form takes a string index name and key selectors:

```csharp
query
    .From("employees")
    .LookupJoin<LogEntry, LanguageLookup, int, object>(
        "languages_lookup",
        outer => outer.StatusCode,
        inner => inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

When outer and inner key selectors reference the same field name, a simple `ON` clause is generated:

```csharp
outer => outer.ClientIp,
inner => inner.ClientIp,
// ON clientIp (instead of ON clientIp == clientIp)
```

### LookupJoin with predicate

Use an expression-based `ON` condition for more complex join logic:

```csharp
query
    .From("employees")
    .LookupJoin<LogEntry, LanguageLookup, object>(
        "languages_lookup",
        (outer, inner) => outer.StatusCode == inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

### LeftJoin with IQueryable inner

Use `LeftJoin` when the inner source is another `EsqlQueryable` with `.From()`:

```csharp
var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

query
    .From("employees")
    .LeftJoin(
        lookup,
        outer => outer.StatusCode,
        inner => inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

### Standard LINQ Join (inner join)

The standard `Queryable.Join` is also supported. It translates to `LOOKUP JOIN` followed by `WHERE key IS NOT NULL` to enforce inner join semantics:

```csharp
query
    .From("firewall_logs")
    .Join(
        lookup,
        outer => outer.ClientIp,
        inner => inner.ClientIp,
        (outer, inner) => new { outer.Message, inner.ThreatLevel }
    )
```

```
FROM firewall_logs
| LOOKUP JOIN threat_list ON clientIp
| WHERE clientIp IS NOT NULL
| KEEP message, threatLevel
```

### LINQ query syntax (left outer join)

The standard LINQ query syntax for left outer joins works naturally:

```csharp
var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

var esql = (
    from outer in query.From("employees")
    join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
    from inner in ps.DefaultIfEmpty()
    select new { outer.Message, inner!.LanguageName }
).ToString();
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

### Complex projections in join result selectors

The result selector supports computed fields, renames, and null guards:

```csharp
// Computed field → EVAL
(outer, inner) => new { Msg = outer.Message, Lang = inner!.LanguageName.ToUpperInvariant() }
// | RENAME message AS msg
// | EVAL lang = TO_UPPER(languageName)
// | KEEP msg, lang

// Null guard → unwraps to simple field access
(outer, inner) => new { LanguageName = inner == null ? null : inner.LanguageName }
// | KEEP languageName
```

### Chaining multiple joins

```csharp
query
    .From("system_metrics")
    .LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
        "host_inventory", outer => outer.ClientIp, inner => inner.ClientIp, (o, i) => o)
    .LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
        "ownerships", outer => outer.ServerName, inner => inner.ClientIp, (o, i) => o)
```

```
FROM system_metrics
| LOOKUP JOIN host_inventory ON clientIp
| LOOKUP JOIN ownerships ON serverName == clientIp
```

## ROW - literal values

The `.Row()` extension method produces a `ROW` source command with literal values:

```csharp
query
    .Row(() => new { a = 1, b = "hello" })
    .ToString()
```

```
ROW a = 1, b = "hello"
```

This is primarily used with `COMPLETION` for standalone LLM prompts without querying an index.

## COMPLETION - LLM inference

`.Completion()` translates to the ES|QL `COMPLETION` command. It sends a field to a configured inference endpoint and returns the result as a new column. See the [COMPLETION docs](completion.md) for full details.

```csharp
query
    .Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")
```

```
FROM logs-*
| COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
```

The lambda overload resolves field names from your type. A string overload is also available for raw field names:

```csharp
query.Completion("message", "my-custom-endpoint", column: "result")
```

## MultiField access

Access sub-fields of multi-field mappings using `.MultiField()`:

```csharp
.Where(l => l.Message.MultiField("keyword") == "exact match")
// WHERE message.keyword == "exact match"
```

## Named parameterization

Captured C# variables can be parameterized instead of inlined:

```csharp
var minStatus = 400;
var level = "ERROR";

var esql = query
    .Where(l => l.StatusCode >= minStatus && l.Level == level)
    .ToEsqlString(inlineParameters: false);
```

```
FROM logs-*
| WHERE (statusCode >= ?minStatus AND log.level == ?level)
```

Parameters are extracted separately via `.GetParameters()` for passing to the ES|QL API. When `inlineParameters` is `true` (the default), values are embedded directly in the query string.

## Unsupported operations

| LINQ method | Reason |
|---|---|
| `.Skip()` | ES\|QL does not support offset-based pagination |
| `.Distinct()` | Use `.GroupBy()` instead |
| Nested subqueries | ES\|QL does not support subqueries |
