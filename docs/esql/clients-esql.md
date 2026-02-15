---
navigation_title: Clients.Esql
---

# Elastic.Clients.Esql

`Elastic.Clients.Esql` adds query execution to Elastic.Esql. It connects LINQ-based query translation to a real Elasticsearch cluster via `Elastic.Transport`, handling HTTP communication, authentication, and result materialization.

## Install

```shell
dotnet add package Elastic.Clients.Esql
```

This pulls in `Elastic.Esql` and `Elastic.Transport` automatically.

## Setup

### Minimal

```csharp
var client = new EsqlClient(new Uri("https://my-cluster:9200"));
```

### With authentication

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(
        new Uri("https://my-cluster:9200"),
        new ApiKey("your-api-key")
    )
);
var client = new EsqlClient(new EsqlClientSettings(transport));
```

### With mapping context (AOT safe)

```csharp
var settings = new EsqlClientSettings(transport)
{
    MappingContext = MyContext.Instance,
    Defaults = new EsqlQueryDefaults
    {
        TimeZone = "UTC",
        Columnar = false
    }
};
var client = new EsqlClient(settings);
```

### In-memory mode (testing, string generation)

```csharp
var client = EsqlClient.InMemory(MyContext.Instance);

// Generates ES|QL without connecting to a cluster
var esql = client.Query<Product>()
    .Where(p => p.InStock)
    .ToString();
```

## Querying

### LINQ fluent syntax

```csharp
var results = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR" && l.Duration > 500)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToListAsync();
```

### LINQ query syntax

```csharp
var results = await (
    from l in client.Query<LogEntry>()
    where l.Level == "ERROR"
    orderby l.Timestamp descending
    select new { l.Message, l.Duration }
).ToListAsync();
```

### Lambda expression

```csharp
var results = await client.QueryAsync<LogEntry>(q =>
    q.Where(l => l.Level == "ERROR")
     .OrderByDescending(l => l.Timestamp)
     .Take(10)
);
```

### Raw ES|QL string

```csharp
var results = await client.QueryAsync<LogEntry>(
    """FROM logs-* | WHERE log.level == "ERROR" | LIMIT 10"""
);
```

### Override index pattern

```csharp
var results = await client.Query<Product>("products-*")
    .Where(p => p.Price > 100)
    .ToListAsync();
```

## Standalone completion

`CompletionAsync<T>()` executes a standalone `ROW + COMPLETION` query for LLM inference without querying an index:

```csharp
var results = await client.CompletionAsync<CompletionResult>(
    "Summarize the benefits of Elasticsearch",
    InferenceEndpoints.OpenAi.Gpt41,
    column: "answer"
);
```

See the [COMPLETION docs](completion.md) for pipeline patterns, well-known endpoints, and the `CompletionQuery` static factory.

## Scalar and single-value queries

```csharp
var count = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .CountAsync();

var hasErrors = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .AnyAsync();

var first = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR")
    .FirstOrDefaultAsync();
```

## Async queries

Long-running queries can be submitted asynchronously. The cluster returns a query ID that you poll for completion.

```csharp
await using var asyncQuery = await client.QueryAsyncQuery<LogEntry>(
    q => q.Where(l => l.Level == "ERROR"),
    new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    }
);

if (asyncQuery.IsRunning)
    Console.WriteLine($"Query {asyncQuery.QueryId} still running...");

var results = await asyncQuery.ToListAsync();
// Query is automatically deleted from the cluster on dispose
```

## Inspect generated ES|QL

Call `.ToString()` or `.ToEsqlString()` on any query to see the generated ES|QL without executing it:

```csharp
var query = client.Query<Product>()
    .Where(p => p.Price > 100)
    .OrderBy(p => p.Name);

Console.WriteLine(query.ToString());
// FROM products
// | WHERE price > 100
// | SORT name
```

## Result materialization

Responses from Elasticsearch come back as rows with typed columns. `EsqlClient` automatically maps these to your C# types by matching column names to properties (using `[JsonPropertyName]` attributes or camelCase convention). Enums, nullable types, and date conversions are handled automatically.
