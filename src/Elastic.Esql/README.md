# Elastic.Esql

Write LINQ, get ES|QL. A pure translation library that converts C# LINQ expressions into Elasticsearch ES|QL query strings. **No HTTP dependencies, no transport layer, AOT compatible** -- just query generation.

## Why?

ES|QL is powerful but building query strings by hand is error-prone. **Elastic.Esql** lets you write idiomatic C# and get correct, optimized ES|QL -- with full IntelliSense, compile-time checking, and refactoring support.

```csharp
var esql = new EsqlQueryable<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR" && l.Duration > 1000)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToString();
```

Produces:

```
FROM logs-*
| WHERE (log.level == "ERROR" AND duration > 1000)
| SORT @timestamp DESC
| LIMIT 50
```

## Quick Start

### Translation-only (no Elasticsearch connection needed)

```csharp
// Reflection-based field resolution
var query = new EsqlQueryable<Order>()
    .From("orders");

// Or with a source-generated JsonSerializerContext -- AOT safe
var provider = new EsqlQueryProvider(MyContext.Default);
query = new EsqlQueryable<Order>(provider)
    .From("orders");

var esql = query
    .Where(o => o.Status == "shipped" && o.Total > 100)
    .OrderByDescending(o => o.CreatedAt)
    .Take(25)
    .ToString();
```

### LINQ query syntax works too

```csharp
var esql = (
    from o in new EsqlQueryable<Order>().From("orders")
    where o.Status == "shipped"
    where o.Total > 100
    orderby o.CreatedAt descending
    select new { o.Id, o.Total, o.CreatedAt }
).ToString();
```

```
FROM orders
| WHERE status == "shipped"
| WHERE total > 100
| SORT created_at DESC
| KEEP id, total, created_at
```

## What Translates?

### Filtering

```csharp
.Where(l => l.StatusCode >= 500)                          // WHERE statusCode >= 500
.Where(l => l.Level == "ERROR" || l.Level == "FATAL")     // WHERE (log.level == "ERROR" OR log.level == "FATAL")
.Where(l => !l.IsResolved)                                // WHERE NOT isResolved
.Where(l => tags.Contains(l.Tag))                         // WHERE tag IN ("a", "b", "c")
```

### Sorting

```csharp
.OrderBy(l => l.Level).ThenByDescending(l => l.Timestamp) // SORT log.level, @timestamp DESC
```

### Projection

```csharp
.Select(l => new { l.Message, Secs = l.Duration / 1000 }) // KEEP message | EVAL secs = (duration / 1000)
```

### Aggregation

```csharp
.GroupBy(l => l.Level)
.Select(g => new {
    Level = g.Key,
    Count = g.Count(),
    AvgDuration = g.Average(l => l.Duration)
})
// STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level
```

### String methods

```csharp
.Where(l => l.Message.Contains("timeout"))                 // WHERE message LIKE "*timeout*"
.Where(l => l.Host.StartsWith("prod-"))                    // WHERE host LIKE "prod-*"
.Where(l => string.IsNullOrEmpty(l.Tag))                   // WHERE (tag IS NULL OR tag == "")
```

### DateTime -- properties, arithmetic, and static members all translate

```csharp
.Where(l => l.Timestamp.Year == 2025)                      // WHERE DATE_EXTRACT("year", @timestamp) == 2025
.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))    // WHERE @timestamp > (NOW() - 1 hours)
.Select(l => new { Hour = l.Timestamp.Hour })               // EVAL hour = DATE_EXTRACT("hour_of_day", @timestamp)
```

Comparisons against `DayOfWeek` values are remapped automatically -- ES|QL `day_of_week` uses ISO numbering (Monday = 1 to Sunday = 7), while .NET `DayOfWeek` starts at Sunday = 0. `l.Timestamp.DayOfWeek == DayOfWeek.Sunday` translates to `DATE_EXTRACT("day_of_week", @timestamp) == 7`.

### Math

```csharp
.Where(l => Math.Abs(l.Delta) > 0.5)                       // WHERE ABS(delta) > 0.5
.Select(l => new { Root = Math.Sqrt(l.Value) })             // EVAL root = SQRT(value)
```

### ES|QL-specific functions

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;

.Where(l => Match(l.Message, "connection error"))           // WHERE MATCH(message, "connection error")
.Where(l => CidrMatch(l.ClientIp, "10.0.0.0/8"))           // WHERE CIDR_MATCH(client_ip, "10.0.0.0/8")
.Where(l => Like(l.Path, "/api/v?/users"))                  // WHERE path LIKE "/api/v?/users"
```

### Vector and hybrid search

`KNN`, `TEXT_EMBEDDING`, dense vector similarity (`V_COSINE`, `V_DOT_PRODUCT`, `V_HAMMING`, `V_L1_NORM`, `V_L2_NORM`), `FROM ... METADATA`, and `FORK` + `FUSE` for hybrid lexical + semantic search are all supported. Vectors are passed as `DenseVector<T>` (with `T = float` or `T = byte`); implicit conversions from `T[]` and `ReadOnlyMemory<T>` keep call sites natural, and the bundled JSON converter handles the signed-byte wire format for byte vectors.

```csharp
// KNN with metadata-driven scoring
.From("books", MetadataField.Score)
.Where(b => EsqlFunctions.Knn(b.Embedding, queryVec, new KnnOptions { K = 10 }))
.OrderByDescending(_ => EsqlMetadata.Score)
// FROM books METADATA _score | WHERE KNN(embedding, [...], { "k": 10 }) | SORT _score DESC

// Hybrid lexical + semantic with FORK + FUSE
.From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
.Fork(
    b => b.Where(x => EsqlFunctions.Match(x.Title, "shakespeare")).Take(50),
    b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, queryVec)).Take(50))
.Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.7, 0.3])
```

The `MetadataField` flags enum selects which document metadata fields to request via the `METADATA` directive (`_id`, `_score`, `_source`, etc.); the `EsqlMetadata` static marker class exposes them for use inside `Where` / `OrderBy` / `Select` / `Fuse` lambdas.

## AOT Compatible

Elastic.Esql has no dependency on `Elastic.Transport` or any HTTP library. The entire translation pipeline -- expression visitors, query model, ES|QL generation -- is pure computation with no reflection-based serialization, no dynamic code generation, and no runtime type emission.

When constructed with a source-generated `JsonSerializerContext`, the full path from LINQ expression to ES|QL string is AOT safe.

## Execution

Elastic.Esql is a pure translation library -- it generates ES|QL strings but does not execute them. Use **Elastic.Clients.Esql** for the official `Elastic.Transport`-based execution layer, or implement `IEsqlQueryExecutor` and pass it to the `EsqlQueryProvider` constructor to plug in your own transport:

```csharp
// MyExecutor implements Elastic.Esql.Execution.IEsqlQueryExecutor
var provider = new EsqlQueryProvider(MyContext.Default, new MyExecutor());

var results = await new EsqlQueryable<Order>(provider)
    .From("orders")
    .Where(o => o.Total > 100)
    .AsEsqlQueryable()
    .ToListAsync();
```

`AsEsqlQueryable()` casts the chain back to `IEsqlQueryable<T>` after standard LINQ operators have returned the base `IQueryable<T>` interface, which makes the async execution methods available.

Without an execution-capable provider, queries translate to strings only -- calling `ToListAsync()` throws. This is by design.

## Field Name Resolution

Field names resolve through `System.Text.Json` metadata. Pass a source-generated `JsonSerializerContext` so field names derive from the same compile-time source of truth as your serialization contracts, with zero reflection at runtime:

```csharp
// Field names come from [JsonPropertyName] attributes or the
// PropertyNamingPolicy of your serializer context (camelCase by default)
var provider = new EsqlQueryProvider(MyContext.Default);
var query = new EsqlQueryable<Product>(provider)
    .From("products")
    .Where(p => p.Name.Contains("laptop"))  // Resolves to the JSON field name
    .ToEsqlString();
```

Without an explicit context, field names are resolved via reflection using `[JsonPropertyName]` attributes or the camelCase naming convention.
