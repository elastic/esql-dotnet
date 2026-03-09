---
navigation_title: Translation Package
---

# Elastic.Esql

A translation library that converts C# LINQ expressions into Elasticsearch [ES|QL](elasticsearch://reference/query-languages/esql.md) query strings. No HTTP dependencies, no transport layer, AOT compatible -- just query generation.

## When to use this package

Use `Elastic.Esql` directly when you need:

- ES|QL string generation without any HTTP dependency
- Query inspection or logging of generated ES|QL
- Integration with a transport layer you already have

If you want LINQ-to-ES|QL with real cluster execution, use [Elastic.Clients.Esql](package-client.md) instead -- it includes this package automatically.

## Install

```shell
dotnet add package Elastic.Esql
```

## Quick start

```csharp
var query = new EsqlQueryable<Order>()
    .From("orders")
    .Where(o => o.Status == "shipped" && o.Total > 100)
    .OrderByDescending(o => o.CreatedAt)
    .Take(25)
    .ToString();
```

Produces:

```
FROM orders
| WHERE (status == "shipped" AND total > 100)
| SORT createdAt DESC
| LIMIT 25
```

### With a JsonSerializerContext (AOT safe)

For Native AOT compatibility, pass a source-generated `JsonSerializerContext` so field names are resolved without reflection:

```csharp
[JsonSerializable(typeof(Order))]
public partial class MyJsonContext : JsonSerializerContext;

var provider = new EsqlQueryProvider(MyJsonContext.Default);
var query = new EsqlQueryable<Order>(provider)
    .From("orders")
    .Where(o => o.Status == "shipped")
    .ToString();
```

### With JsonSerializerOptions

You can pass `JsonSerializerOptions` to control field name resolution:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
var provider = new EsqlQueryProvider(options);
var query = new EsqlQueryable<Order>(provider)
    .From("orders")
    .Where(o => o.Status == "shipped")
    .ToString();
```

### LINQ query syntax

```csharp
var esql = (
    from o in new EsqlQueryable<Order>().From("orders")
    where o.Status == "shipped"
    where o.Total > 100
    orderby o.CreatedAt descending
    select new { o.Id, o.Total, o.CreatedAt }
).ToString();
```

## What translates?

See the [functions reference](functions-reference.md) for the complete list and [LINQ translation](linq-translation.md) for how each LINQ command maps to ES|QL.

### Filtering

```csharp
.Where(l => l.StatusCode >= 500)                          // WHERE statusCode >= 500
.Where(l => l.Level == "ERROR" || l.Level == "FATAL")     // WHERE (level == "ERROR" OR level == "FATAL")
.Where(l => !l.IsResolved)                                // WHERE NOT isResolved
.Where(l => tags.Contains(l.Tag))                         // WHERE tag IN ("a", "b", "c")
```

### Sorting and limiting

```csharp
.OrderBy(l => l.Level).ThenByDescending(l => l.Timestamp) // SORT level, @timestamp DESC
.Take(50)                                                  // LIMIT 50
```

### Projection

```csharp
.Select(l => new { l.Message, Secs = l.Duration / 1000 }) // EVAL secs = (duration / 1000) | KEEP message, secs
```

### KEEP and DROP

```csharp
.Keep("message", "@timestamp")                             // KEEP message, @timestamp
.Keep(l => l.Message, l => l.Timestamp)                    // KEEP message, @timestamp
.Drop("duration", "host")                                  // DROP duration, host
.Drop(l => l.Duration, l => l.Host)                        // DROP duration, host
```

### Aggregation

```csharp
.GroupBy(l => l.Level)
.Select(g => new {
    Level = g.Key,
    Count = g.Count(),
    AvgDuration = g.Average(l => l.Duration)
})
// STATS count = COUNT(*), avgDuration = AVG(duration) BY level
```

### String functions

```csharp
.Where(l => l.Message.Contains("timeout"))                 // WHERE message LIKE "*timeout*"
.Where(l => l.Host.StartsWith("prod-"))                    // WHERE host LIKE "prod-*"
.Where(l => l.Message.ToLower() == "error")                // WHERE TO_LOWER(message) == "error"
```

### DateTime

```csharp
.Where(l => l.Timestamp.Year == 2025)                      // WHERE DATE_EXTRACT("year", @timestamp) == 2025
.Where(l => l.Timestamp > DateTime.UtcNow.AddHours(-1))    // WHERE @timestamp > (NOW() + -1 hours)
.Select(l => new { Hour = l.Timestamp.Hour })               // EVAL hour = DATE_EXTRACT("hour", @timestamp)
```

### Math

```csharp
.Where(l => Math.Abs(l.Delta) > 0.5)                       // WHERE ABS(delta) > 0.5
.Select(l => new { Root = Math.Sqrt(l.Value) })             // EVAL root = SQRT(value)
```

### LLM completion

```csharp
.Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")

.Row(() => new { prompt = "Tell me about Elasticsearch" })
.Completion("prompt", InferenceEndpoints.Anthropic.Claude46Opus, column: "answer")
```

See the [COMPLETION docs](completion.md) for pipeline patterns, standalone completions, and well-known endpoint constants.

### ES|QL-specific functions

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;

.Where(l => Match(l.Message, "connection error"))           // WHERE MATCH(message, "connection error")
.Where(l => CidrMatch(l.ClientIp, "10.0.0.0/8"))           // WHERE CIDR_MATCH(client_ip, "10.0.0.0/8")
.Where(l => Like(l.Path, "/api/v?/users"))                  // WHERE path LIKE "/api/v?/users"
```

### LOOKUP JOIN

```csharp
query.LookupJoin<Order, Customer, string, OrderWithCustomer>(
    "customers",
    o => o.CustomerId,
    c => c.Id,
    (o, c) => new OrderWithCustomer { Order = o, CustomerName = c.Name }
)
// LOOKUP JOIN customers ON customer_id
```

### Named parameters

Captured C# variables can be parameterized instead of inlined:

```csharp
var minStatus = 400;
var esql = query
    .Where(l => l.StatusCode >= minStatus)
    .ToEsqlString(inlineParameters: false);
// WHERE statusCode >= ?minStatus

var parameters = query.GetParameters();
```

## Execution

Elastic.Esql is a pure translation library -- it generates ES|QL strings but does not execute them. Use [Elastic.Clients.Esql](package-client.md) for the official `Elastic.Transport`-based execution layer, or implement the `IEsqlQueryExecutor` interface to plug in your own transport.
