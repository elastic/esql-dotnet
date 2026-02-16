---
navigation_title: ES|QL LINQ
---

# ES|QL LINQ for .NET

Write C# LINQ expressions, get Elasticsearch [ES|QL](elasticsearch://reference/query-languages/esql.md) query strings. Type-safe, AOT compatible, with full IntelliSense and compile-time checking.

```csharp
var results = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR" && l.Duration > 1000)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToListAsync();
```

Produces:

```
FROM logs-*
| WHERE (log.level.keyword == "ERROR" AND duration > 1000)
| SORT @timestamp DESC
| LIMIT 50
```

## Two packages, one goal

The ES|QL LINQ support is split into two NuGet packages so you can choose the right level of dependency for your project.

**Most projects should install `Elastic.Clients.Esql`** — it includes everything you need to build and execute ES|QL queries against an Elasticsearch cluster:

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
| [Elastic.Esql](package-translation.md)    | LINQ-to-ES                                                 | QL translation only, zero dependencies                         | You need string generation, a custom transport, or query inspection |

## Works with Elastic.Mapping

When paired with [`Elastic.Mapping`](../mapping/index.md)'s source-generated field resolution, field names resolve from your mapping context instead of reflection — fully AOT safe:

```csharp
var query = new EsqlQueryable<Product>(MyContext.Instance)
    .Where(p => p.Name.Contains("laptop"))
    .ToString();
```

Without Elastic.Mapping, field names are resolved via reflection using `[JsonPropertyName]` attributes or camelCase convention.