---
navigation_title: ES|QL LINQ
---

# ES|QL LINQ for .NET

Write C# LINQ expressions, get Elasticsearch [ES|QL](elasticsearch://reference/query-languages/esql.md) query strings. Type-safe, AOT compatible, with full IntelliSense and compile-time checking.

```csharp
var results = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR" && l.Duration > 1000)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToListAsync();
```

Produces:

```
FROM logs-*
| WHERE (log.level == "ERROR" AND duration > 1000)
| SORT @timestamp DESC
| LIMIT 50
```

## Two packages, one goal

The ES|QL LINQ support is split into two NuGet packages so you can choose the right level of dependency for your project.

**Most projects should install `Elastic.Clients.Esql`** -- it includes everything you need to build and execute ES|QL queries against an Elasticsearch cluster:

```shell
dotnet add package Elastic.Clients.Esql
```

This pulls in `Elastic.Esql` and `Elastic.Transport` automatically.

If you only need query string generation without any HTTP or transport dependency, install the translation library directly:

```shell
dotnet add package Elastic.Esql
```

| Package                                   | What it does                                               | When to use it                                                 |
|-------------------------------------------|------------------------------------------------------------|----------------------------------------------------------------|
| [Elastic.Clients.Esql](package-client.md) | LINQ translation + query execution via `Elastic.Transport` | You want to run queries against a cluster and get results back |
| [Elastic.Esql](package-translation.md)    | LINQ-to-ES\|QL translation only, zero dependencies        | You need string generation, a custom transport, or query inspection |

## Translation deep dive

- [LINQ command mapping reference](linq-translation.md)
- [LINQ translation architecture](linq-translation-architecture.md)

## Field name resolution

Field names are resolved automatically from your C# types using `System.Text.Json` conventions:

1. **`[JsonPropertyName]` attributes** -- if a property has `[JsonPropertyName("log.level")]`, that exact name is used in the generated ES|QL
2. **`JsonNamingPolicy`** -- if a `JsonNamingPolicy` is configured (e.g., `JsonNamingPolicy.CamelCase`), property names are transformed accordingly
3. **Default convention** -- without explicit configuration, property names are used as-is with camelCase conversion

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }  // → @timestamp

    [JsonPropertyName("log.level")]
    public string Level { get; set; }        // → log.level

    public string Message { get; set; }      // → message (camelCase)

    public long Duration { get; set; }       // → duration (camelCase)
}
```

### AOT-safe field resolution

For Native AOT compatibility, pass a source-generated `JsonSerializerContext` to the query provider. This ensures field names are resolved at compile time with zero reflection:

```csharp
[JsonSerializable(typeof(LogEntry))]
public partial class MyJsonContext : JsonSerializerContext;

var provider = new EsqlQueryProvider(MyJsonContext.Default);
var query = new EsqlQueryable<LogEntry>(provider)
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .ToString();
```
