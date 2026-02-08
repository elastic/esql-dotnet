# Elastic.Esql

Write LINQ, get ES|QL. A pure translation library that converts C# LINQ expressions into Elasticsearch ES|QL query strings. **No HTTP dependencies, no transport layer, AOT compatible** -- just query generation.

## Why?

ES|QL is powerful but building query strings by hand is error-prone. **Elastic.Esql** lets you write idiomatic C# and get correct, optimized ES|QL -- with full IntelliSense, compile-time checking, and refactoring support.

```csharp
var esql = new EsqlQueryable<LogEntry>()
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
var query = new EsqlQueryable<Order>();

// Or with a source-generated mapping context (from Elastic.Mapping) -- AOT safe
var query = new EsqlQueryable<Order>(MyContext.Instance);

var esql = query
    .Where(o => o.Status == "shipped" && o.Total > 100)
    .OrderByDescending(o => o.CreatedAt)
    .Take(25)
    .ToString();
```

### LINQ query syntax works too

```csharp
var esql = (
    from o in new EsqlQueryable<Order>()
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
.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))    // WHERE @timestamp > DATE_ADD("hours", -1, NOW())
.Select(l => new { Hour = l.Timestamp.Hour })               // EVAL hour = DATE_EXTRACT("hour", @timestamp)
```

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

## AOT Compatible

Elastic.Esql has no dependency on `Elastic.Transport` or any HTTP library. The entire translation pipeline -- expression visitors, query model, ES|QL generation -- is pure computation with no reflection-based serialization, no dynamic code generation, and no runtime type emission.

When paired with `Elastic.Mapping`'s source-generated field resolution, the full path from LINQ expression to ES|QL string is AOT safe.

## The IEsqlQueryExecutor Abstraction

Elastic.Esql defines a minimal execution interface so you can plug in any transport:

```csharp
public interface IEsqlQueryExecutor
{
    Task<EsqlResponse> ExecuteAsync(string esql, CancellationToken cancellationToken = default);
}
```

Pass an executor to enable query execution alongside translation:

```csharp
var context = new EsqlQueryContext(mappingContext, myExecutor);
var provider = new EsqlQueryProvider(context);
var results = await new EsqlQueryable<Order>(provider)
    .Where(o => o.Total > 100)
    .ToListAsync();
```

Without an executor, queries translate to strings only -- calling `ToListAsync()` throws. This is by design: use **Elastic.Clients.Esql** for the official Elasticsearch transport implementation, or implement `IEsqlQueryExecutor` yourself.

## Works With Elastic.Mapping

When paired with the `Elastic.Mapping` source generator, field names resolve from your generated mapping context instead of reflection -- fully AOT compatible:

```csharp
// Field names come from [JsonPropertyName], [Text], [Keyword], etc.
// Aligned with your System.Text.Json source-generated serialization context
var query = new EsqlQueryable<Product>(MyContext.Instance)
    .Where(p => p.Name.Contains("laptop"))  // Uses generated field name
    .ToString();
```

Without `Elastic.Mapping`, field names are resolved via reflection using `JsonPropertyName` attributes or camelCase convention.
