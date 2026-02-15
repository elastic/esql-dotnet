# Elastic.Esql

A translation library that converts C# LINQ expressions into Elasticsearch [ES|QL](elasticsearch://reference/query-languages/esql.md) query strings. No HTTP dependencies, no transport layer, AOT compatible - just query generation.

## When to use what

**[Elastic.Clients.Elasticsearch](https://github.com/elastic/elasticsearch-net)** is the official .NET client for Elasticsearch. If you want to run queries against a cluster and get results back, start there. It supports the full Elasticsearch API including search, indexing, cluster management, and more.

**Elastic.Clients.Esql** builds on Elastic.Esql to add query execution via `Elastic.Transport`. Use it when you want LINQ-to-ES|QL with real cluster execution. See the [Elastic.Clients.Esql docs](clients-esql.md).

**Elastic.Esql** (this package) is the translation layer only. Use it when you need:

- ES|QL string generation without any HTTP dependency
- A custom `IEsqlQueryExecutor` implementation
- Query inspection or logging of generated ES|QL
- Integration with a transport layer you already have

## Install

```shell
dotnet add package Elastic.Esql
```

## Quick start

```csharp
var query = new EsqlQueryable<Order>()
    .Where(o => o.Status == "shipped" && o.Total > 100)
    .OrderByDescending(o => o.CreatedAt)
    .Take(25)
    .ToString();
```

Produces:

```
FROM orders
| WHERE (status.keyword == "shipped" AND total > 100)
| SORT created_at DESC
| LIMIT 25
```

### With a mapping context (AOT safe)

```csharp
var query = new EsqlQueryable<Order>(MyContext.Instance)
    .Where(o => o.Status == "shipped")
    .ToString();
```

### LINQ query syntax

```csharp
var esql = (
    from o in new EsqlQueryable<Order>()
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
.Where(l => l.Level == "ERROR" || l.Level == "FATAL")     // WHERE (...keyword == "ERROR" OR ...keyword == "FATAL")
.Where(l => !l.IsResolved)                                // WHERE NOT isResolved
.Where(l => tags.Contains(l.Tag))                         // WHERE tag IN ("a", "b", "c")
```

### Sorting and limiting

```csharp
.OrderBy(l => l.Level).ThenByDescending(l => l.Timestamp) // SORT log.level.keyword, @timestamp DESC
.Take(50)                                                  // LIMIT 50
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
// STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level.keyword
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
// Pipeline: retrieve docs, send to LLM
.Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")

// Standalone: prompt without an index
CompletionQuery.Generate("Tell me about Elasticsearch", InferenceEndpoints.Anthropic.Claude46Opus)
```

See the [COMPLETION docs](completion.md) for pipeline patterns, standalone completions, and well-known endpoint constants.

### ES|QL-specific functions

```csharp
using static Elastic.Esql.Functions.EsqlFunctions;

.Where(l => Match(l.Message, "connection error"))           // WHERE MATCH(message, "connection error")
.Where(l => CidrMatch(l.ClientIp, "10.0.0.0/8"))           // WHERE CIDR_MATCH(client_ip, "10.0.0.0/8")
.Where(l => Like(l.Path, "/api/v?/users"))                  // WHERE path LIKE "/api/v?/users"
```

## Works with Elastic.Mapping

When paired with `Elastic.Mapping`'s source-generated field resolution, field names resolve from your generated mapping context instead of reflection:

```csharp
var query = new EsqlQueryable<Product>(MyContext.Instance)
    .Where(p => p.Name.Contains("laptop"))
    .ToString();
```

Without Elastic.Mapping, field names are resolved via reflection using `[JsonPropertyName]` attributes or camelCase convention.

## The IEsqlQueryExecutor abstraction

Elastic.Esql defines a minimal execution interface so you can plug in any transport:

```csharp
public interface IEsqlQueryExecutor
{
    Task<EsqlResponse> ExecuteAsync(string esql, CancellationToken cancellationToken = default);
}
```

Use [Elastic.Clients.Esql](clients-esql.md) for the default `Elastic.Transport`-based implementation, or implement `IEsqlQueryExecutor` yourself for custom transports.
