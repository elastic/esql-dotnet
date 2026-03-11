# ES|QL LINQ for .NET

Type-safe Elasticsearch [ES|QL](elasticsearch://reference/query-languages/esql.md) development for .NET. Write LINQ expressions, get ES|QL query strings, execute against Elasticsearch. AOT compatible, zero reflection at runtime.

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

[Get started with ES|QL LINQ →](esql/index.md)

## Key features

- **Type-safe ES|QL**: write LINQ, get correct ES|QL with full IntelliSense and compile-time checking
- **80+ translated functions**: math, string, date/time, search, IP, cast, grouping, and aggregation functions
- **LOOKUP JOIN**: correlate data across indices with `LeftJoin` and `LookupJoin`
- **COMPLETION**: run LLM inference directly in ES|QL pipelines with preconfigured inference endpoints
- **Async queries**: submit long-running queries, poll for completion, stream results
- **Named parameters**: extract captured variables as `?param` placeholders for parameterized queries
- **AOT compatible**: zero reflection at runtime, works with `PublishAot=true`
- **System.Text.Json native**: inherits naming policies and serialization behavior from your STJ configuration
- **Streaming results**: consume results as `IAsyncEnumerable<T>` for memory-efficient processing

## Packages

| Package                | Description                                                       |
|------------------------|-------------------------------------------------------------------|
| `Elastic.Esql`         | [LINQ-to-ES\|QL translation library](esql/package-translation.md) |
| `Elastic.Clients.Esql` | [ES\|QL execution via Elastic.Transport](esql/package-client.md)  |
