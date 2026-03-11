# `esql-dotnet`

[![CI](https://github.com/elastic/esql-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/elastic/esql-dotnet/actions/workflows/ci.yml)

Write LINQ, get [ES|QL](https://www.elastic.co/guide/en/elasticsearch/reference/current/esql.html), execute against Elasticsearch. Type-safe, compile-time field resolution, **Native AOT ready**.

## Packages

| Package | Description |
|---------|-------------|
| [`Elastic.Esql`](src/Elastic.Esql) | LINQ-to-ES\|QL translation engine -- no HTTP dependencies, pure query generation |
| [`Elastic.Clients.Esql`](src/Elastic.Clients.Esql) | `EsqlClient` that connects the translation engine to a real cluster via `Elastic.Transport` |

## Quick Start

### 1. Define your domain types

Clean POCOs with `System.Text.Json` attributes:

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("log.level")]
    public string Level { get; set; }

    public string Message { get; set; }

    [JsonPropertyName("service.name")]
    public string ServiceName { get; set; }

    public long Duration { get; set; }
}
```

Field names are resolved from `[JsonPropertyName]` attributes, or via the configured `JsonNamingPolicy` (defaults to camelCase).

### 2. Write LINQ, get ES|QL

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

### 3. Execute against Elasticsearch

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri(url), new ApiKey(apiKey)));

var settings = new EsqlClientSettings(transport);
using var client = new EsqlClient(settings);

var errors = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .OrderByDescending(l => l.Timestamp)
    .Take(10)
    .ToListAsync();
```

## How It Works

```
  Your C# code                          Runtime
┌──────────────┐    ┌──────────────────────────────────────┐
│  POCO types  │    │                                      │
│  + STJ attrs │    │  LINQ expression tree                │
└──────┬───────┘    │         │                            │
       │            │         v                            │
       │            │  ┌──────────────────┐                │
       └───────────>│  │   Elastic.Esql   │  ES|QL string  │
                    │  │  LINQ-to-ES|QL   │────────┐       │
                    │  └──────────────────┘        │       │
                    │                              v       │
                    │  ┌──────────────────┐  ┌───────────┐ │
                    │  │ Elastic.Clients  │  │   HTTP     │ │
                    │  │     .Esql        │─>│ execution  │ │
                    │  └──────────────────┘  │ → results  │ │
                    │                        └───────────┘ │
                    └──────────────────────────────────────┘
```

## What Translates?

| C# LINQ | ES\|QL |
|---------|--------|
| `.Where(l => l.Level == "ERROR")` | `WHERE log.level == "ERROR"` |
| `.OrderByDescending(l => l.Timestamp)` | `SORT @timestamp DESC` |
| `.Take(50)` | `LIMIT 50` |
| `.Select(l => new { l.Message })` | `KEEP message` |
| `.Select(l => new { Secs = l.Duration / 1000 })` | `EVAL secs = (duration / 1000)` |
| `.GroupBy(...).Select(g => new { Count = g.Count() })` | `STATS count = COUNT(*) BY ...` |
| `.Where(l => l.Message.Contains("timeout"))` | `WHERE message LIKE "*timeout*"` |
| `.Where(l => l.Timestamp.Year == 2025)` | `WHERE DATE_EXTRACT("year", @timestamp) == 2025` |
| `.Where(l => Math.Abs(l.Delta) > 0.5)` | `WHERE ABS(delta) > 0.5` |
| `EsqlFunctions.Match(l.Message, "error")` | `MATCH(message, "error")` |
| `.Keep(l => l.Message, l => l.Timestamp)` | `KEEP message, @timestamp` |
| `.Drop("duration", "host")` | `DROP duration, host` |
| `.LeftJoin(...)` / `.LookupJoin(...)` | `LOOKUP JOIN index ON field` |
| `.Completion(l => l.Message, endpoint)` | `COMPLETION col = message WITH {...}` |
| `.Row(() => new { prompt = "..." })` | `ROW prompt = "..."` |

See the [Elastic.Esql README](src/Elastic.Esql) for the full list including string methods, DateTime arithmetic, and ES|QL-specific functions.

## Key Features

### Full LINQ Translation

Translates `.Where()`, `.Select()`, `.GroupBy()`, `.OrderBy()`, `.Take()`, and more into ES|QL commands: `WHERE`, `EVAL`, `KEEP`, `DROP`, `STATS...BY`, `SORT`, `LIMIT`, `RENAME`, `ROW`, `COMPLETION`, and `LOOKUP JOIN`.

### RawEsql Fragment Append

For expert scenarios, append raw ES|QL fragments inline with `.RawEsql(...)` while keeping the existing typed execution pipeline:

```csharp
var rows = client.Query<LogEntry>(q => q
    .From("logs-*")
    .RawEsql("WHERE log.level == \"ERROR\"")
    .RawEsql("| LIMIT 25"));
```

You can also switch the downstream result type with `RawEsql<TSource, TNext>(...)`.

### 80+ ES|QL Functions

Math (`ABS`, `SQRT`, `ROUND`, ...), string (`TRIM`, `CONCAT`, `REPLACE`, ...), date/time (`DATE_EXTRACT`, `DATE_TRUNC`, `NOW`, ...), search (`MATCH`, `KQL`, `QSTR`, ...), IP (`CIDR_MATCH`, `IP_PREFIX`), cast operators (`::integer`, `::keyword`, ...), grouping (`BUCKET`, `CATEGORIZE`), and aggregation (`PERCENTILE`, `MEDIAN`, `STD_DEV`, `VALUES`, ...).

### Async Query Execution

Submit long-running queries asynchronously with `ToAsyncQueryAsync()`. Poll for completion, stream results, and auto-cleanup on dispose:

```csharp
await using var asyncQuery = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .ToAsyncQueryAsync(new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    });

var results = await asyncQuery.ToListAsync();
```

### COMPLETION (LLM Inference)

Run LLM inference directly in ES|QL pipelines or as standalone prompts using preconfigured inference endpoints:

```csharp
// RAG pipeline
client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")

// Standalone
client.CreateQuery<Result>()
    .Row(() => new { prompt = "Summarize Elasticsearch" })
    .Completion("prompt", InferenceEndpoints.Anthropic.Claude46Opus, column: "answer")
```

### LOOKUP JOIN

Correlate data across indices with `LeftJoin` or `LookupJoin`:

```csharp
query.LookupJoin<Order, Customer, string, OrderWithCustomer>(
    "customers",
    o => o.CustomerId,
    c => c.Id,
    (o, c) => new OrderWithCustomer { Order = o, CustomerName = c.Name }
)
```

### Named Parameters

Extract captured variables as named `?param` placeholders instead of inlining them:

```csharp
var minStatus = 400;
var esql = query
    .Where(l => l.StatusCode >= minStatus)
    .ToEsqlString(inlineParameters: false);
// WHERE statusCode >= ?minStatus
```

### Streaming Results

Stream query results with `IAsyncEnumerable<T>`:

```csharp
await foreach (var entry in client.QueryAsync<LogEntry>(q =>
    q.From("logs-*").Where(l => l.Level == "ERROR").Take(100)))
{
    Console.WriteLine(entry.Message);
}
```

### Native AOT

The entire pipeline is AOT compatible. Pass a source-generated `JsonSerializerContext` and field names, serialization, and queries all derive from the same compile-time source of truth with zero reflection at runtime.

```csharp
var provider = new EsqlQueryProvider(MyJsonContext.Default);
var query = new EsqlQueryable<LogEntry>(provider);
```

### Multi-Target

Supports `netstandard2.0`, `net8.0`, and `net10.0` with polyfills for older targets.

## Building

```bash
dotnet build esql-dotnet.slnx

# Run tests (TUnit -- uses dotnet run, not dotnet test)
dotnet run --project tests/Elastic.Esql.Tests

# Or use the build script
./build.sh
```

## Examples

The [`examples/`](examples/) directory contains working applications:

- **`Elastic.Examples.Domain`** -- shared domain types
- **`Elastic.Examples.Esql`** -- ES|QL queries against a live cluster

## License

Apache 2.0. See [LICENSE](LICENSE) for details.
