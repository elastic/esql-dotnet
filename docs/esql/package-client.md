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

When `JsonSerializerContext` is set, it takes precedence over `JsonSerializerOptions`, and the serializer settings of the context's own options (converters, number handling, case sensitivity, and so on) carry over to materialization. You can also set `JsonSerializerOptions` directly for non-AOT scenarios:

```csharp
var settings = new EsqlClientSettings(transport)
{
    JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }
};
```

Types that the context does not declare are resolved through the reflection-based default
resolver. Under Native AOT that fallback still handles types with an explicit `[JsonConverter]`
attribute; any other type must be declared in the context, or materialization fails at runtime.

If neither `JsonSerializerContext` nor `JsonSerializerOptions` is provided, `EsqlClient` defaults to camelCase naming.

The options you pass are made read-only when the first result is materialized, the same way `JsonSerializer` freezes
options on its own first use. Translation makes them read-only earlier when it serializes a named parameter or a literal
through the serializer, such as an integer, decimal, enum or `Guid` literal. The options the client builds itself, the
camelCase defaults and the copy it makes from a `JsonSerializerContext`, are read-only from construction. Register every
converter, naming policy and resolver before running the first query; changing them on an instance that has already been
used throws.

On runtimes that support dynamic code, a directly bound row assigns every cell through a setter compiled for the target
member. Only a public setter or a public field gets a compiled setter, and any other member is assigned through the
contract's `JsonPropertyInfo.Set`, which throws for a member a source-generated context cannot reach. A struct target
gets no compiled setter, so each of its members is assigned through the contract's `JsonPropertyInfo.Set`. A row is
bound directly only when every part of the resolver is a `JsonSerializerContext` produced by the source generator, which
marks it with a `GeneratedCode` attribute, or a plain `DefaultJsonTypeInfoResolver` without modifiers, alone or in a
chain built with `JsonTypeInfoResolver.Combine` or the options' `TypeInfoResolverChain`, such as the client's
combination of a context with the reflection resolver. A resolver built with `JsonTypeInfoResolver.Combine` and added to
`TypeInfoResolverChain` stays one entry of the chain, and its parts are judged the same way. The `TypeInfoResolverChain`
of another options instance does not count, whether it is assigned as the resolver, added to the chain or carried into a
copy made with the `JsonSerializerOptions` copy constructor, because its parts can change after a contract is cached
through them. Assigning `JsonTypeInfoResolver.Combine` over the copied parts as the copy's resolver keeps the direct
path, because `Combine` takes a snapshot of a chain argument's parts. A hand-written `JsonSerializerContext` and a
subclass of the default resolver do not count, since either can supply property metadata or replace
`JsonPropertyInfo.Set` in its own `GetTypeInfo`. A context that carries `[JsonSerializable]` without the generator's
marker, such as one not declared `partial`, counts as hand-written, and so does a subclass of a source-generated
context. Those attributes are the sign the library reads, and they are the contract: a context admitted to the direct
path is one the source generator produced. A hand-written class that declares the generator's attributes itself is
outside that contract, because every member the generator emits can be declared by hand and nothing else tells the two
apart. The direct path then reads each cell through the contract the options resolve for the property's type, whatever
type info such a class supplies for the property, and the row can differ from what the serializer reads. Any other
resolver keeps every result type on the fallback described under How rows are materialized. That includes a context or a
default resolver wrapped by `JsonTypeInfoResolver.WithAddedModifier`. The decision is made the first time a result type
is read with a given set of columns, with the options already read-only.

A cell that its own contract rejects, or a converter result of null for a property that does not accept null, is read
again by the serializer so the error carries the property path. A deserialization callback on a nested type in that cell
or an earlier cell of the row therefore runs twice before such a row fails.

## Querying

### LINQ fluent syntax

```csharp
var results = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR" && l.Duration > 500)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .AsEsqlQueryable()
    .ToListAsync();
```

### LINQ query syntax

```csharp
var results = await (
    from l in client.CreateQuery<LogEntry>().From("logs-*")
    where l.Level == "ERROR"
    orderby l.Timestamp descending
    select new { l.Message, l.Duration }
).AsEsqlQueryable().ToListAsync();
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

## Per-query options

Use `.WithOptions()` to attach options to individual queries.

### Sync / streaming queries

```csharp
var results = await client.CreateQuery<LogEntry>()
    .WithOptions(new EsqlQueryOptions { TimeZone = "America/New_York", Locale = "en-US" })
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .Take(50)
    .AsEsqlQueryable()
    .ToListAsync();
```

It works with all execution styles -- lambda, query syntax, and streaming:

```csharp
await foreach (var entry in client.QueryAsync<LogEntry>(q => q
     .AsEsqlQueryable()
     .WithOptions(new EsqlQueryOptions { TimeZone = "UTC" })
     .From("logs-*")
     .Where(l => l.Level == "ERROR")))
{
    Console.WriteLine(entry.Message);
}
```

### Async queries

For async queries, `EsqlAsyncQueryOptions` controls the async submission behavior. Can be used together with `.WithOptions()`:

```csharp
await using var asyncQuery = await client.CreateQuery<LogEntry>()
    .WithOptions(new EsqlQueryOptions { TimeZone = "UTC" })
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .ToAsyncQueryAsync(new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    });

await asyncQuery.WaitForCompletionAsync();
var results = asyncQuery.ToList();
```

Or via the `EsqlClient` convenience methods:

```csharp
await using var asyncQuery = await client.SubmitAsyncQueryAsync<LogEntry>(
    q => q.AsEsqlQueryable()
          .WithOptions(new EsqlQueryOptions { TimeZone = "UTC" })
          .From("logs-*")
          .Where(l => l.Level == "ERROR"),
    new EsqlAsyncQueryOptions { KeepOnCompletion = true }
);
```

### Available options

Protocol-level options live on `EsqlQueryOptions` (from `Elastic.Esql`) and work with any executor:

| Option | Type | Description |
|---|---|---|
| `AllowPartialResults` | `bool?` | Allow partial results when shards are unavailable |
| `DropNullColumns` | `bool?` | Omit columns where every value is null from the response |
| `TimeZone` | `string?` | Timezone for date operations (e.g., `"UTC"`, `"America/New_York"`) |
| `Locale` | `string?` | Locale for formatting (e.g., `"en-US"`) |

Transport-level options live on `EsqlTransportOptions`:

| Option | Type | Description |
|---|---|---|
| `RequestConfiguration` | `IRequestConfiguration?` | Per-request transport overrides |

`EsqlTransportOptions` is specific to `Elastic.Clients.Esql`. Other downstream implementations may define their own `WithOptions` extensions with different option types; such methods must be marked with `[EsqlQueryOptionsMethod]` (from `Elastic.Esql.Extensions`) so the translator recognizes them. Setting the same options slot more than once in a query chain throws `InvalidOperationException`.

### Transport-level overrides

Use `RequestConfiguration` to control transport behavior per query -- for example, custom timeouts, authentication, or headers:

```csharp
var results = await client.CreateQuery<LogEntry>()
    .WithOptions(new EsqlTransportOptions
    {
        RequestConfiguration = new RequestConfiguration
        {
            RequestTimeout = TimeSpan.FromSeconds(120),
            Authentication = new BasicAuthentication("user", "pass"),
            Headers = new NameValueCollection { { "X-Custom-Header", "value" } }
        }
    })
    .From("logs-*")
    .AsEsqlQueryable()
    .ToListAsync();
```

The `RequestConfiguration` is forwarded to all transport calls -- including poll and delete operations for async queries.

## Scalar and single-value queries

```csharp
var count = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .CountAsync();

var hasErrors = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .AnyAsync();

var first = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .FirstOrDefaultAsync();

var single = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .Take(1)
    .AsEsqlQueryable()
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
var query = client.CreateQuery<LogEntry>().From("logs-*").Take(100).AsEsqlQueryable();

await foreach (var entry in query.AsAsyncEnumerable())
{
    ProcessEntry(entry);
}
```

### How rows are materialized

Flat result types are bound directly from the response reader, property by property, without an intermediate JSON
document. A type qualifies for this path when all of the following hold:

- every part of the options' resolver chain is a source-generated `JsonSerializerContext` or a plain
  `DefaultJsonTypeInfoResolver` without modifiers, which options without a resolver get at the first read, judged the
  first time a result type is read with a given set of columns, because any other part can supply property metadata the
  direct path cannot see (AOT-safe configuration lists which resolvers count)
- it is a class or struct that the serializer creates through a parameterless constructor
- it has no `OnDeserializing` or `OnDeserialized` callback of its own
- every result column maps to a settable property that is not the extension data property
- neither the type nor a mapped property sets its own `JsonNumberHandling`
- no mapped property uses `JsonObjectCreationHandling.Populate`, whether set on the property, the type or the options
- every `required` member has a column
- no mapped property binds as a converter cell while the options' `MaxDepth` is 1, because a cell reader needs one level
  below the row
- no mapped property binds as a converter cell while the options set a `ReferenceHandler` other than
  `ReferenceHandler.IgnoreCycles`, because each converter cell is read on its own and would not see `$id` or `$ref`
  metadata from another cell
- no column maps to a property whose JSON name starts with `$` while the options set a `ReferenceHandler` other than
  `ReferenceHandler.IgnoreCycles` or the type carries `[JsonPolymorphic]` or `[JsonDerivedType]`, because the serializer
  can read such a column as metadata. Under such a handler it reads `$id` and `$ref` as reference metadata, which must
  hold a string, and rejects any other name with a leading `$`, and without one it rejects every such name on a type
  with type discriminators
- no column's name equals, case included, the discriminator name that `[JsonPolymorphic]` sets through
  `TypeDiscriminatorPropertyName`, because the serializer reads such a column as the type discriminator, in first
  position under the default options and in any position under `AllowOutOfOrderMetadataProperties`, and never binds it
  to the property
- no two columns map to the same property
- every column is flat, so a dotted name such as `address.city` that resolves into a nested object keeps the type on the
  fallback

A mapped `string`, `bool`, `int`, `long`, `double`, `float`, `decimal`, `DateTime`, `DateTimeOffset` or `Guid` property,
or the nullable form of one of these, is parsed straight off the reader when the serializer's stock converter for that
type reads it, the converter `JsonMetadataServices` exposes for the type. Another converter of `System.Text.Json`, such
as the object converter a factory can return for `string`, keeps the property on the converter cell. A `List<string>` or
`string[]` property without a `[JsonConverter]` attribute is read off the reader as well when the serializer's stock
list or array converter, the one `JsonMetadataServices.CreateListInfo` or `CreateArrayInfo` creates, and its stock
string converter read it and the `MaxDepth` and `ReferenceHandler` conditions above for a converter cell hold. A
source-generated context in the default or serialization generation mode reads these types through a wrapper around that
converter, and a property it reads that way is read off the reader as well unless the options hold a converter for the
property type or a user-written converter factory, because the wrapper does not expose the converter it holds. Another
collection converter of `System.Text.Json` registered for the type, such as the `IList` converter
`JsonMetadataServices.CreateIListInfo` creates, keeps the property on the converter cell. A bare string cell becomes a
one-element list where the column name matches the property's JSON name exactly, as on the fallback, and a null element
stays null. An enum property without a `[JsonConverter]` attribute, or the nullable form of one, on a `byte`, `integer`,
`long`, `short` or `unsigned_long` column is read off the reader too when the options read its type with a converter of
`System.Text.Json` that accepts numbers, which `JsonStringEnumConverter` does unless `allowIntegerValues` is false. A
number in the range of the enum's underlying type binds whether or not the enum defines it, as in the serializer, and
the serializer reads any other cell. A converter registered on the options for the nullable form of a type, or any
user-written converter factory on the options, keeps a nullable value type on the converter cell, because such a
converter can be, and such a factory can return, the serializer's nullable wrapper around a converter of its own, and a
factory's `CanConvert` need not give the same answer twice. A user-written factory therefore costs every nullable scalar
or enum property a converter cell, while the serializer's own factories, `JsonStringEnumConverter` among them, do not
count. Any other property binds as a converter cell, deserialized from the cell alone through the property's
`[JsonConverter]` when it has one and through the contract of its type otherwise. Other enums, other collections,
dictionaries, nested objects, types with a converter registered on the `JsonSerializerOptions` and properties with a
`[JsonConverter]` attribute all bind this way. A converter cell is read by a reader of its own over the cell's bytes, so
a converter that reads the reader's depth or position sees the cell and not the row, and one that returns `CurrentDepth`
reads 0 on the direct path where the serializer reads 1. The direct path cannot tell such a converter apart, so a
converter whose result depends on either reads differently there. A property-level converter needs a runtime that
supports dynamic code, so under Native AOT a type with one stays on the fallback. It must also be a converter for the
property type itself, after a factory is expanded. The direct path expands the factory itself, apart from the
serializer's expansion for the contract and possibly more than once, so it must return a converter with the same
behavior for the same type and options. A factory-made collection, dictionary or nested-object converter of the
serializer's own reads on the direct path only when it is the converter instance the type's own contract carries, the
one a factory that returns `options.GetConverter(type)` hands back on read-only options. The reader makes the options
read-only before it binds the first row, and a contract resolved earlier, by a query that names a member of the type, is
not kept, so the direct path reads the contracts the serializer uses. Any other such converter keeps the type on the
fallback, where the serializer applies it. That includes a new instance of the serializer's nullable wrapper or of a
converter that `JsonMetadataServices` creates, because only the instance the own contract carries is known to read as
that contract does. A property whose type the resolver does not resolve keeps the type on the fallback as well when it
has such a converter. A converter declared for a base type keeps the type on the fallback, where the serializer applies
it through its casting converter. The compiled setters described under AOT-safe configuration need dynamic code too, and
without it a directly bound row assigns its cells through the contract's own `JsonPropertyInfo.Set`.

When a converter that does not come from `System.Text.Json` reads a property whose type is a collection, `byte[]`
included, a single-valued cell reaches it wrapped in an array, the shape a multi-valued cell has, so the converter must
accept an array.

A type that misses any condition in the list falls back to assembling each row as a JSON object and deserializing it
with `System.Text.Json`. The fallback is correct but slower, and nothing signals which path is active. If throughput
matters, shape the result type to the list above. A row whose cell does not match the property type is retried through
the fallback, so the serializer produces the same value coercion or error either way. A streamed row cut by a chunk
boundary is read again on the direct path once it completes. For a type without converter, string list or enum cells,
each retried or re-read row allocates and discards one partially bound instance, so a constructor or property setter
with side effects runs more than once for such rows. A type with converter, string list or enum cells checks the whole
row before it creates the instance, so it discards one only when a converter cell fails after that check.

Nested result types are deserialized in batches of up to 64 rows or 64 KB, so the first row
becomes available after the first batch rather than immediately. Batching also requires the
serializer to resolve type metadata for `List<T>` through the framework's own list converter; a
source-generated context that does not declare `List<T>` for the result type, or options that
register a custom converter for `List<T>`, stream rows one at a time instead. When a row inside a
batch is malformed or does not match the type, the rows before it are still delivered and the
error surfaces at the faulty row, the same as for flat types.

A single-column query such as `Select(l => l.Timestamp)` binds each cell to the result type when the serializer reads
that type as a value or a collection, for example `DateTime` or `List<string>`. A cell of one of the scalar types listed
above is read off the reader under the same resolver and converter conditions as a row property, and through the
serializer otherwise. A result type the serializer reads as an object or a dictionary binds the whole row, as it does
for a wider query. So do `JsonDocument`, `JsonElement`, `JsonElement?`, `JsonNode`, `JsonObject` and `object`, which
receive the row as an object with the column as its one property. `JsonArray` and `JsonValue` cannot hold an object, so
they bind the cell.

## Raw response formats

By default, every query path materialises rows into POCOs by streaming the JSON response through the typed reader. For scenarios where you want the unparsed, server-formatted bytes — piping to a file, feeding columnar consumers like Apache Arrow / DataFusion / DuckDB, or doing your own zero-copy decoding — every queryable also exposes raw-stream terminal methods.

Pick the wire format with the `EsqlFormat` enum:

| Value | Media type | Notes |
|---|---|---|
| `Json` | `application/json` | Same envelope the typed reader consumes, but returned unparsed |
| `Csv` | `text/csv` | One header row plus data |
| `Tsv` | `text/tab-separated-values` | Tab-delimited variant |
| `Txt` | `text/plain` | Human-readable ASCII table |
| `Arrow` | `application/vnd.apache.arrow.stream` | Apache Arrow IPC stream |
| `Smile` | `application/smile` | Binary JSON variant |
| `Cbor` | `application/cbor` | CBOR |
| `Yaml` | `application/yaml` | YAML |

### Sync raw queries

```csharp
using var stream = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .ToStreamAsync(EsqlFormat.Csv);

await stream.CopyToAsync(File.Create("errors.csv"));
```

`ToStreamAsync(format)` returns a `Stream`. Disposing the stream releases the underlying HTTP connection — the response wrapper is owned by the returned stream.

A synchronous overload `ToStream(format)` is available for non-async call sites. On .NET 10+, `ToPipeReaderAsync(format)` returns a `PipeReader` for zero-copy consumers. The `format` argument is optional on all three methods; when omitted, the query's configured format applies, defaulting to JSON.

### Raw async queries

For long-running queries with a non-JSON format, use `ToAsyncQueryAsync(format)`. This mirrors `ToAsyncQueryAsync()` but returns a non-generic `EsqlAsyncQuery` because there is no `T` to materialise into:

```csharp
await using var q = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
    .ToAsyncQueryAsync(EsqlFormat.Arrow);

await q.WaitForCompletionAsync();

using var stream = q.GetResponseStream();
using var reader = new ArrowStreamReader(stream); // Apache.Arrow.Ipc — separate NuGet
while (await reader.ReadNextRecordBatchAsync() is { } batch)
    Console.WriteLine($"Batch: {batch.Length} rows, {batch.ColumnCount} cols");
```

Disposing the `EsqlAsyncQuery` releases the held response and issues a best-effort `DELETE /_query/async/{id}`. The delete still runs when releasing the response throws.

Calling `GetResponseStream()` before completion throws `InvalidOperationException`. Use `RefreshAsync()` for a single poll or `WaitForCompletionAsync()` to poll until done (default 100 ms interval).

## Async queries

Long-running queries can be submitted asynchronously. The cluster returns a query ID that you can poll for completion. The `EsqlAsyncQuery<T>` type manages the lifecycle and auto-deletes the query from the cluster on dispose.

### Submit and wait

```csharp
await using var asyncQuery = await client.SubmitAsyncQueryAsync<LogEntry>(
    q => q.From("logs-*").Where(l => l.Level == "ERROR"),
    new EsqlAsyncQueryOptions
    {
        WaitForCompletionTimeout = TimeSpan.FromSeconds(5),
        KeepAlive = TimeSpan.FromMinutes(10)
    }
);

// Wait for completion if still running, then get results
await asyncQuery.WaitForCompletionAsync();
var results = asyncQuery.ToList();
```

### Poll manually

```csharp
await using var asyncQuery = await client.CreateQuery<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .AsEsqlQueryable()
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
using var asyncQuery = client.SubmitAsyncQuery<LogEntry>(
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

The polling interval for `WaitForCompletion` / `WaitForCompletionAsync` can be set via the `pollInterval` parameter (default: 100ms):

```csharp
await asyncQuery.WaitForCompletionAsync(pollInterval: TimeSpan.FromMilliseconds(500));
```

## Completion queries

Use `ROW` + `COMPLETION` in the LINQ pipeline for standalone prompts:

```csharp
var results = await client.CreateQuery<CompletionResult>()
    .Row(() => new { prompt = "Summarize the benefits of Elasticsearch" })
    .Completion("prompt", InferenceEndpoints.OpenAi.Gpt41, column: "answer")
    .AsEsqlQueryable()
    .ToListAsync();
```

See the [COMPLETION docs](completion.md) for pipeline patterns and well-known endpoint IDs.

## Vector and hybrid search

Run KNN, exact similarity, and hybrid (lexical + semantic) search using `DenseVector<T>` vector parameters and the `Fork(...)` / `Fuse(...)` extensions:

```csharp
var queryVec = new float[] { 0.12f, -0.03f, 0.98f /* ... */ };

var results = await client.CreateQuery<Book>()
    .From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
    .Fork(
        b => b.Where(x => EsqlFunctions.Match(x.Title, "shakespeare")).Take(50),
        b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, queryVec)).Take(50))
    .Fuse()
    .OrderByDescending(_ => EsqlMetadata.Score)
    .Take(10)
    .AsEsqlQueryable()
    .ToListAsync();
```

See the [vector and hybrid search docs](vector-search.md) for the full API surface, including KNN options, `TEXT_EMBEDDING`, `V_*` similarity functions, FORK/FUSE configuration, and `EsqlMetadata` markers.

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
        .AsEsqlQueryable()
        .ToListAsync();
}
catch (EsqlExecutionException ex)
{
    Console.WriteLine($"Status: {ex.StatusCode}");
    Console.WriteLine($"Response: {ex.ResponseBody}");
}
```
