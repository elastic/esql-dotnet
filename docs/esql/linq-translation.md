---
navigation_title: LINQ translation
---

# LINQ to ES|QL translation

How LINQ operators map to [ES|QL commands](elasticsearch://reference/query-languages/esql.md). Elastic.Esql uses expression visitors to translate LINQ method chains into the ES|QL pipe-delimited command syntax.

## FROM - source resolution

Every query starts with `FROM`. The source index pattern is resolved from your type registration:

```csharp
// Index target
[Entity<Product>(Target = EntityTarget.Index, Name = "products", SearchPattern = "products*")]

// Data stream target
[Entity<AppLog>(Target = EntityTarget.DataStream, Type = "logs", Dataset = "myapp", Namespace = "production")]
```

| Registration | FROM output |
|---|---|
| `[Entity<T>(Target = EntityTarget.Index, SearchPattern = "logs-*")]` | `FROM logs-*` |
| `[Entity<T>(Target = EntityTarget.DataStream, Type = "logs", Dataset = "myapp")]` | `FROM logs-myapp-*` |
| No registration (convention) | `FROM {type-name}` |

When using a mapping context, the search pattern is resolved from `SearchStrategy.Pattern`. Without a context, field names fall back to reflection with `[JsonPropertyName]` or camelCase convention.

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
| WHERE log.level.keyword == "ERROR"
```

### Text field handling

String fields mapped as `text` automatically get a `.keyword` suffix for equality and comparison operations. This ensures exact matching against the keyword sub-field:

```csharp
.Where(l => l.Level == "ERROR")       // log.level.keyword == "ERROR" (text field, exact match)
.Where(l => l.StatusCode >= 500)       // statusCode >= 500 (numeric, no suffix)
```

The `.keyword` suffix is **not** added for:

- `MATCH()` - uses full-text analysis on the text field
- Null checks (`== null`, `!= null`) - nullness applies to the entire field
- `LIKE` / `RLIKE` patterns

### Null handling

```csharp
.Where(l => l.Tag == null)             // WHERE tag IS NULL
.Where(l => l.Tag != null)             // WHERE tag IS NOT NULL
```

### Compound conditions

```csharp
.Where(l => l.Level == "ERROR" && l.Duration > 1000)
// WHERE (log.level.keyword == "ERROR" AND duration > 1000)

.Where(l => l.Level == "ERROR" || l.Level == "FATAL")
// WHERE (log.level.keyword == "ERROR" OR log.level.keyword == "FATAL")

.Where(l => !(l.StatusCode >= 500))
// WHERE NOT (statusCode >= 500)
```

### IN operator

```csharp
var levels = new[] { "ERROR", "FATAL", "CRITICAL" };
query.Where(l => levels.Contains(l.Level))
// WHERE log.level.keyword IN ("ERROR", "FATAL", "CRITICAL")
```

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
| STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level.keyword
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
| STATS count = COUNT(*) BY level = log.level.keyword, host = host.keyword
```

### Terminal aggregation operators

Aggregation methods called directly on the queryable produce `STATS` without `BY`:

```csharp
query.Where(l => l.Level == "ERROR").Count()
// FROM logs-* | WHERE log.level.keyword == "ERROR" | STATS count = COUNT(*)

query.Sum(l => l.Duration)
// FROM logs-* | STATS sum = SUM(duration)
```

## SORT - ordering

`.OrderBy()` and `.OrderByDescending()` translate to the ES|QL [`SORT`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-sort) command:

```csharp
query
    .OrderBy(l => l.Level)
    .ThenByDescending(l => l.Timestamp)
```

```
FROM logs-*
| SORT log.level.keyword, @timestamp DESC
```

Text fields get the `.keyword` suffix for consistent lexical sorting.

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
// | EVAL status = CASE(statusCode >= 500, "error", "ok")
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
| WHERE (statusCode >= ?minStatus AND log.level.keyword == ?level)
```

Parameters are extracted separately via `.GetParameters()` for passing to the ES|QL API. When `inlineParameters` is `true` (the default), values are embedded directly in the query string.

## Unsupported operations

| LINQ method | Reason |
|---|---|
| `.Skip()` | ES|QL does not support offset-based pagination |
| `.Distinct()` | Use `.GroupBy()` instead |
| Nested subqueries | ES|QL does not support subqueries |
