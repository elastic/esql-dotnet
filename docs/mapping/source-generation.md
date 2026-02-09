---
navigation_title: Source generation
---

# Source generation

The Elastic.Mapping source generator produces compile-time infrastructure for each registered type. This page covers the generated output, hash-based change detection, and integration with Elastic.Ingest.

## Generated structure

For a context like:

```csharp
[ElasticsearchMappingContext]
[Entity<Product>(Target = EntityTarget.Index, Name = "products")]
public static partial class MyContext;
```

The generator produces:

```
MyContext (static partial class)
├── Product (ProductResolver instance)
│   ├── Fields (FieldNames)
│   │   ├── .Id          → "id"
│   │   ├── .Name        → "name"
│   │   └── .Price       → "price"
│   ├── FieldMapping (FieldMappings)
│   │   ├── .PropertyToField   → Dictionary<string, string>
│   │   └── .FieldToProperty   → Dictionary<string, string>
│   ├── Context (ElasticsearchTypeContext)
│   │   ├── .GetSettingsJson()
│   │   ├── .GetMappingsJson()
│   │   └── .GetIndexJson()
│   ├── IndexStrategy
│   │   └── .WriteTarget        → "products"
│   ├── SearchStrategy
│   │   └── .Pattern            → "products*"
│   ├── Hash                    → "a1b2c3d4e5f6a7b8"
│   ├── SettingsHash
│   └── MappingsHash
├── All → IReadOnlyList<ElasticsearchTypeContext>
└── Instance → IElasticsearchMappingContext
```

## Hash-based change detection

The generator computes SHA256 hashes of the generated JSON, enabling efficient change detection without comparing full JSON payloads.

### Three hash types

| Hash | Covers | Use case |
|---|---|---|
| `Hash` | Settings + mappings combined | Overall change detection |
| `SettingsHash` | Index settings only | Infrastructure changes (shards, refresh interval) |
| `MappingsHash` | Field mappings only | Schema changes (new fields, type changes) |

### How hashes are computed

```
1. Generate settings JSON and mappings JSON
2. Minify (remove whitespace for stable comparison)
3. Prefix with generator version: "v1:{minified-json}"
4. Compute SHA256
5. Take first 16 hex characters
```

The version prefix ensures hashes change when the generator changes its output format, even if the source types haven't changed.

### Change detection example

```csharp
// Compare against stored hash from cluster
var storedHash = await GetStoredHashFromCluster("products");

if (storedHash != MyContext.Product.Hash)
{
    // Mappings have changed - update the index
    var json = MyContext.Product.Context.GetIndexJson();
    await UpdateIndex("products", json);
    await StoreHash("products", MyContext.Product.Hash);
}
```

## ElasticsearchTypeContext

The `ElasticsearchTypeContext` record provides access to all generated metadata for a type:

```csharp
public record ElasticsearchTypeContext(
    Func<string> GetSettingsJson,
    Func<string> GetMappingsJson,
    Func<string> GetIndexJson,
    string Hash,
    string SettingsHash,
    string MappingsHash,
    IndexStrategy? IndexStrategy,
    SearchStrategy? SearchStrategy,
    EntityTarget EntityTarget,
    DataStreamMode DataStreamMode = DataStreamMode.Default,
    Func<object, string?>? GetId = null,
    Func<object, string?>? GetContentHash = null,
    Func<object, DateTimeOffset?>? GetTimestamp = null,
    Func<AnalysisBuilder, AnalysisBuilder>? ConfigureAnalysis = null,
    Type? MappedType = null
);
```

### JSON output methods

| Method | Returns |
|---|---|
| `GetSettingsJson()` | Index settings (shards, replicas, refresh interval, analysis) |
| `GetMappingsJson()` | Field mappings (properties, dynamic templates, runtime fields) |
| `GetIndexJson()` | Combined settings + mappings, ready for `PUT /{index}` |

## IndexStrategy and SearchStrategy

### IndexStrategy (write targets)

```csharp
MyContext.Product.IndexStrategy.WriteTarget       // "products" or write alias
MyContext.Product.IndexStrategy.DataStreamName     // For data streams
MyContext.Product.IndexStrategy.GetWriteTarget()   // Resolves the target
MyContext.Product.IndexStrategy.GetWriteTarget(DateTime.UtcNow)  // With date pattern
```

| Property | Description |
|---|---|
| `WriteTarget` | Concrete index name or write alias |
| `DatePattern` | Rolling date pattern (e.g., `yyyy.MM.dd`) |
| `DataStreamName` | Full data stream name (`{type}-{dataset}-{namespace}`) |
| `Type` | Data stream type component |
| `Dataset` | Data stream dataset component |
| `Namespace` | Data stream namespace component |

### SearchStrategy (read targets)

```csharp
MyContext.Product.SearchStrategy.Pattern           // "products*"
MyContext.Product.SearchStrategy.ReadAlias          // ILM read alias
MyContext.Product.SearchStrategy.GetSearchTarget()  // Resolves the target
```

## Iterating all registered types

The `All` property provides access to every registered type's context:

```csharp
foreach (var typeContext in MyContext.All)
{
    var json = typeContext.GetIndexJson();
    var hash = typeContext.Hash;
    var writeTarget = typeContext.IndexStrategy?.WriteTarget;
    // Create or update index...
}
```

## Integration with Elastic.Ingest

Hash-based change detection enables schema drift detection in ingestion pipelines:

```csharp
// On startup, compare hashes to detect schema changes
foreach (var ctx in MyContext.All)
{
    var clusterHash = await GetClusterMappingsHash(ctx.IndexStrategy);

    if (clusterHash != ctx.MappingsHash)
    {
        // Schema has drifted - update mappings
        await PutMappings(ctx);
    }

    if (clusterHash != ctx.SettingsHash)
    {
        // Settings changed - may need index recreation
        await RecreateIndex(ctx);
    }
}
```
