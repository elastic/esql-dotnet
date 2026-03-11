---
navigation_title: Client Package
---

# Using the Elastic.Clients.Esql package

`Elastic.Clients.Esql` adds query execution to Elastic.Esql. It connects LINQ-based query translation to a real Elasticsearch cluster via `Elastic.Transport`, handling HTTP communication, authentication, and result materialization.

## Install

```shell
dotnet add package Elastic.Clients.Esql
```

This pulls in `Elastic.Esql` and `Elastic.Transport` automatically.

## Setup

### Minimal

```csharp
using var client = new EsqlClient(new Uri("https://my-cluster:9200"));
```

### With authentication

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(
        new Uri("https://my-cluster:9200"),
        new ApiKey("your-api-key")
    )
);
using var client = new EsqlClient(new EsqlClientSettings(transport));
```

### With connection pool

```csharp
var pool = new StaticNodePool(new[]
{
    new Uri("https://node1:9200"),
    new Uri("https://node2:9200")
});
using var client = new EsqlClient(new EsqlClientSettings(pool));
```

### With default query options

```csharp
var settings = new EsqlClientSettings(transport)
{
    Defaults = new EsqlQueryDefaults
    {
        TimeZone = "UTC",
        Locale = "en-US"
    }
};
using var client = new EsqlClient(settings);
```

### AOT-safe configuration

For Native AOT, supply a source-generated `JsonSerializerContext` to control how result types are materialized:

```csharp
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(Product))]
public partial class MyJsonContext : JsonSerializerContext;

var settings = new EsqlClientSettings(transport)
{
    JsonSerializerContext = MyJsonContext.Default
};
using var client = new EsqlClient(settings);
```

When `JsonSerializerContext` is set, it takes precedence over `JsonSerializerOptions`. You can also set `JsonSerializerOptions` directly for non-AOT scenarios:

```csharp
var settings = new EsqlClientSettings(transport)
{
    JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }
};
```

If neither `JsonSerializerContext` nor `JsonSerializerOptions` is provided, `EsqlClient` defaults to camelCase naming.

## Querying

### LINQ fluent syntax

```csharp
var results = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR" && l.Duration > 500)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToListAsync();
```

### LINQ query syntax

```csharp
var results = await (
    from l in client.CreateQuery<LogEntry>().From("logs-*")
    where l.Level == "ERROR"
    orderby l.Timestamp descending
    select new { l.Message, l.Duration }
).ToListAsync();
```

### Lambda expression

```csharp
await foreach (var entry in client.QueryAsync<LogEntry>(q =>
    q.From("logs-*")
     .Where(l => l.Level == "ERROR")
     .OrderByDescending(l => l.Timestamp)
     .Take(10)))
{
    Console.WriteLine(entry.Message);
}
```

### Synchronous execution

```csharp
var results = client.Query<LogEntry>(q =>
    q.From("logs-*")
     .Where(l => l.Level == "ERROR")
     .Take(10));
```

### With projection

```csharp
await foreach (var item in client.QueryAsync<LogEntry, dynamic>(q =>
    q.From("logs-*")
     .Where(l => l.Level == "ERROR")
     .Select(l => new { l.Message, l.Duration })))
{
    Console.WriteLine(item);
}
```

### Raw ES|QL fragments

Use `RawEsql()` to append expert-level ES|QL fragments directly in a query pipeline:

```csharp
var results = client.Query<LogEntry>(q => q
    .From("logs-*")
    .RawEsql("WHERE statusCode >= 500")
    .RawEsql("| LIMIT 10"));
```

You can also switch the downstream materialization type:

```csharp
var rows = client.Query<LogEntry, LogProjection>(q => q
    .From("logs-*")
    .RawEsql<LogEntry, LogProjection>("KEEP message, statusCode"));
```

For Native AOT, include the target type (`LogProjection` in this example) in your source-generated `JsonSerializerContext`.

## Scalar and single-value queries

```csharp
var count = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .CountAsync();

var hasErrors = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AnyAsync();

var first = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .FirstOrDefaultAsync();

var single = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .Take(1)
    .SingleAsync();
```

## Streaming results

All async query methods return `IAsyncEnumerable<T>`, enabling memory-efficient streaming of large result sets:

```csharp
await foreach (var entry in client.QueryAsync<LogEntry>(q =>
    q.From("logs-*").Take(10000)))
{
    ProcessEntry(entry);
}
```

You can also get an `IAsyncEnumerable<T>` from any queryable:

```csharp
var query = client.CreateQuery<LogEntry>().From("logs-*").Take(100);

await foreach (var entry in query.AsAsyncEnumerable())
{
    ProcessEntry(entry);
}
```

## Async queries

Long-running queries can be submitted asynchronously. The cluster returns a query ID that you can poll for completion. The `EsqlAsyncQuery<T>` type manages the lifecycle and auto-deletes the query from the cluster on dispose.

### Submit and wait

```csharp
await using var asyncQuery = await client.QueryAsyncQueryAsync<LogEntry>(
    q => q.From("logs-*").Where(l => l.Level == "ERROR"),
    new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    }
);

// Wait for completion if still running, then get results
var results = await asyncQuery.ToListAsync();
```

### Poll manually

```csharp
await using var asyncQuery = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .ToAsyncQueryAsync(new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(1),
        KeepOnCompletion = true
    });

if (asyncQuery.IsRunning)
{
    Console.WriteLine($"Query {asyncQuery.QueryId} still running...");
    await asyncQuery.WaitForCompletionAsync();
}

await foreach (var entry in asyncQuery.AsAsyncEnumerable())
{
    Console.WriteLine(entry.Message);
}
```

### Synchronous async queries

```csharp
using var asyncQuery = client.QueryAsyncQuery<LogEntry>(
    q => q.From("logs-*").Where(l => l.Level == "ERROR"));

asyncQuery.WaitForCompletion();
var results = asyncQuery.ToList();
```

### Async query options

| Option | Default | Description |
|---|---|---|
| `WaitForCompletionTimeout` | 1s | How long to wait before returning an async query ID |
| `KeepAlive` | 5d | How long to keep results on the cluster |
| `KeepOnCompletion` | `false` | Whether to keep results even if completed within the timeout |
| `PollInterval` | 100ms | Polling cadence used while waiting for async query completion |

## Completion queries

Use `ROW` + `COMPLETION` in the LINQ pipeline for standalone prompts:

```csharp
var results = await client.CreateQuery<CompletionResult>()
    .Row(() => new { prompt = "Summarize the benefits of Elasticsearch" })
    .Completion("prompt", InferenceEndpoints.OpenAi.Gpt41, column: "answer")
    .ToListAsync();
```

See the [COMPLETION docs](completion.md) for pipeline patterns and well-known endpoint IDs.

## Inspect generated ES|QL

Call `.ToString()` or `.ToEsqlString()` on any query to see the generated ES|QL without executing it:

```csharp
var query = client.CreateQuery<Product>()
    .From("products")
    .Where(p => p.Price > 100)
    .OrderBy(p => p.Name);

Console.WriteLine(query.ToString());
// FROM products
// | WHERE price > 100
// | SORT name
```

Use `.ToEsqlString(inlineParameters: false)` to see named parameter placeholders, and `.GetParameters()` to extract the parameter values:

```csharp
var minPrice = 100;
var query = client.CreateQuery<Product>()
    .From("products")
    .Where(p => p.Price > minPrice);

Console.WriteLine(query.ToEsqlString(inlineParameters: false));
// FROM products
// | WHERE price > ?minPrice

var parameters = query.GetParameters();
```

## Result materialization

Responses from Elasticsearch come back as rows with typed columns. `EsqlClient` automatically maps these to your C# types by matching column names to properties (using `[JsonPropertyName]` attributes or the configured naming policy). Enums, nullable types, and date conversions are handled automatically.

## Error handling

Transport and execution errors are thrown as `EsqlExecutionException`, which includes the HTTP status code and response body:

```csharp
try
{
    var results = await client.CreateQuery<LogEntry>()
        .From("logs-*")
        .ToListAsync();
}
catch (EsqlExecutionException ex)
{
    Console.WriteLine($"Status: {ex.StatusCode}");
    Console.WriteLine($"Response: {ex.ResponseBody}");
}
```
