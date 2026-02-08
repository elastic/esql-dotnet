# Elastic.Clients.Esql

Execute ES|QL queries against Elasticsearch with LINQ. This package connects the **Elastic.Esql** translation engine to a real cluster via **Elastic.Transport**. Pair with `Elastic.Mapping` for a fully **AOT-compatible** query pipeline from LINQ expression to typed results.

## Why?

`Elastic.Esql` translates LINQ to ES|QL strings. This package adds the HTTP layer to actually *run* them -- connection pooling, authentication, error handling, async queries, and result materialization into typed C# objects.

## Quick Start

```csharp
using var client = new EsqlClient(new Uri("https://my-cluster:9200"));

// LINQ query -- translates and executes in one step
var errors = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .OrderByDescending(l => l.Timestamp)
    .Take(10)
    .ToListAsync();
```

## Configuration

```csharp
// Simple -- just a URI
using var client = new EsqlClient(new Uri("https://localhost:9200"));

// With authentication and mapping context
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri(url), new ApiKey(apiKey)));

var settings = new EsqlClientSettings(transport)
{
    MappingContext = MyContext.Instance,  // From Elastic.Mapping source generator
    Defaults = new EsqlQueryDefaults
    {
        TimeZone = "America/New_York",
        Locale = "en-US"
    }
};
using var client = new EsqlClient(settings);
```

## Query Patterns

### LINQ fluent syntax

```csharp
var topBrands = await client.Query<Product>()
    .Where(p => p.InStock)
    .GroupBy(p => p.Brand)
    .Select(g => new { Brand = g.Key, Avg = g.Average(p => p.Price), Count = g.Count() })
    .OrderByDescending(x => x.Avg)
    .Take(5)
    .ToListAsync();
```

### LINQ query syntax

```csharp
var results = await (
    from log in client.Query<LogEntry>()
    where log.Level == "ERROR"
    where log.Duration > 500
    orderby log.Timestamp descending
    select new { log.Message, log.Duration }
).ToListAsync();
```

### Lambda expression

```csharp
var results = await client.QueryAsync<LogEntry>(q =>
    q.Where(l => l.Level == "ERROR").Take(10));
```

### Raw ES|QL

```csharp
var results = await client.QueryAsync<LogEntry>(
    "FROM logs-* | WHERE log.level == \"ERROR\" | LIMIT 10");
```

### Inspect the generated query

Every queryable's `.ToString()` returns the ES|QL without executing:

```csharp
var query = client.Query<Product>()
    .Where(p => p.Price > 100)
    .OrderBy(p => p.Name);

Console.WriteLine(query.ToString());
// FROM products
// | WHERE price > 100
// | SORT name
```

## Async Queries

For long-running queries, use the async query API. Results auto-delete from the cluster on dispose:

```csharp
await using var asyncQuery = await client.QueryAsyncQuery<LogEntry>(
    q => q.Where(l => l.Level == "ERROR"),
    new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    });

if (asyncQuery.IsRunning)
    Console.WriteLine($"Query {asyncQuery.QueryId} still running...");

var results = await asyncQuery.ToListAsync();  // Polls until complete
// Query automatically deleted from cluster when disposed
```

## Per-Query Options

Override client defaults on individual queries:

```csharp
var results = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .WithTimeZone("Europe/London")
    .WithProfile()
    .WithColumnar()
    .ToListAsync();
```

## Testing

Generate ES|QL strings without an Elasticsearch connection:

```csharp
var client = EsqlClient.InMemory(MyContext.Instance);

var esql = client.Query<Product>()
    .Where(p => p.InStock && p.Price < 50)
    .OrderBy(p => p.Name)
    .ToString();

Assert.Equal("""
    FROM products
    | WHERE (in_stock == true AND price < 50)
    | SORT name
    """, esql);
```

## AOT Compatible

The full pipeline is AOT ready when used with `Elastic.Mapping`:

- **Query translation** (`Elastic.Esql`) -- pure expression tree walking, no reflection-based serialization
- **Field resolution** (`Elastic.Mapping`) -- source-generated constants aligned with your `System.Text.Json` context
- **HTTP transport** (`Elastic.Transport`) -- AOT-compatible HTTP client

Link your `JsonSerializerContext` to `[ElasticsearchMappingContext]` and field names, JSON serialization, and ES|QL queries all derive from the same compile-time source of truth.

## Architecture

```
Elastic.Clients.Esql          -- This package: EsqlClient, transport, execution
  references:
    Elastic.Esql               -- LINQ-to-ES|QL translation (no HTTP dependency)
    Elastic.Mapping            -- Type metadata and field resolution (source generator)
    Elastic.Transport          -- Low-level HTTP client for Elasticsearch
```

If you only need ES|QL string generation (no execution), depend on `Elastic.Esql` directly.
