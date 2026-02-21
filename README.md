# `esql-dotnet`

[![CI](https://github.com/elastic/esql-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/elastic/esql-dotnet/actions/workflows/ci.yml)

Write LINQ, get [ES|QL](https://www.elastic.co/guide/en/elasticsearch/reference/current/esql.html), execute against Elasticsearch. Type-safe, compile-time field resolution, **Native AOT ready**.

## Packages

| Package | Description |
|---------|-------------|
| [`Elastic.Mapping`](src/Elastic.Mapping) | Source generator for Elasticsearch index mappings, field constants, and analysis chains |
| [`Elastic.Esql`](src/Elastic.Esql) | LINQ-to-ES\|QL translation engine -- no HTTP dependencies, pure query generation |
| [`Elastic.Clients.Esql`](src/Elastic.Clients.Esql) | `EsqlClient` that connects the translation engine to a real cluster via `Elastic.Transport` |

## Quick Start

### 1. Define your domain types

Clean POCOs with field-type attributes:

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    [Date]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("log.level")]
    [Keyword]
    public string Level { get; set; }

    [Text]
    public string Message { get; set; }

    [JsonPropertyName("service.name")]
    [Keyword]
    public string ServiceName { get; set; }

    public long Duration { get; set; }
}
```

### 2. Register a mapping context

```csharp
[ElasticsearchMappingContext]
[Entity<LogEntry>(Target = EntityTarget.DataStream, Type = "logs", Dataset = "myapp", Namespace = "production")]
[Entity<Product>(Target = EntityTarget.Index, Name = "products", SearchPattern = "products*")]
public static partial class MyContext;
```

The source generator produces type-safe field constants, index strategies, mappings JSON, and content hashes -- all at compile time.

### 3. Write LINQ, get ES|QL

```csharp
var esql = new EsqlQueryable<LogEntry>(MyContext.Instance)
    .Where(l => l.Level == "ERROR" && l.Duration > 1000)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToString();
```

Produces:

```
FROM logs-myapp-production
| WHERE (log.level == "ERROR" AND duration > 1000)
| SORT @timestamp DESC
| LIMIT 50
```

### 4. Execute against Elasticsearch

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri(url), new ApiKey(apiKey)));

var settings = new EsqlClientSettings(transport)
{
    MappingContext = MyContext.Instance
};
using var client = new EsqlClient(settings);

var errors = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .OrderByDescending(l => l.Timestamp)
    .Take(10)
    .ToListAsync();
```

## How It Works

```
  Your C# code          Compile time              Runtime
┌──────────────┐    ┌──────────────────┐    ┌──────────────────┐
│  POCO types  │───>│ Elastic.Mapping  │    │                  │
│  + attributes│    │ Source Generator  │    │                  │
└──────────────┘    └───────┬──────────┘    │                  │
                            │               │                  │
                    field constants,         │                  │
                    mappings JSON,           │                  │
                    index strategies         │                  │
                            │               │                  │
                            v               │                  │
                    ┌──────────────────┐    │                  │
                    │   Elastic.Esql   │───>│  LINQ expression │
                    │  LINQ-to-ES|QL   │    │  → ES|QL string  │
                    └───────┬──────────┘    │                  │
                            │               │                  │
                            v               │                  │
                    ┌──────────────────┐    │                  │
                    │ Elastic.Clients  │───>│  HTTP execution  │
                    │     .Esql        │    │  → typed results │
                    └──────────────────┘    └──────────────────┘
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

See the [Elastic.Esql README](src/Elastic.Esql) for the full list including string methods, DateTime arithmetic, and ES|QL-specific functions.

## Native AOT

The entire pipeline is AOT compatible. `Elastic.Mapping` generates all field metadata at compile time, `Elastic.Esql` translates via expression tree walking with no reflection-based serialization, and `Elastic.Transport` provides an AOT-safe HTTP client. Link a `System.Text.Json` source-generated `JsonSerializerContext` to `[ElasticsearchMappingContext]` and field names, serialization, and queries all derive from the same compile-time source of truth.

## Building

```bash
dotnet build esql-dotnet.slnx

# Run tests (TUnit — uses dotnet run, not dotnet test)
dotnet run --project tests/Elastic.Esql.Tests

# Or use the build script
./build.sh
```

## Examples

The [`examples/`](examples/) directory contains working applications:

- **`Elastic.Examples.Domain`** -- shared domain types and mapping context
- **`Elastic.Examples.Mapping`** -- index creation and mapping inspection
- **`Elastic.Examples.Ingest`** -- bulk data ingestion with index and data stream strategies
- **`Elastic.Examples.Esql`** -- ES|QL queries against a live cluster

## License

Apache 2.0. See [LICENSE](LICENSE) for details.
