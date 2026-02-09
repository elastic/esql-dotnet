---
navigation_title: AOT & trimming
---

# AOT & trimming support

Every feature in Elastic.Mapping is AOT compatible. The source generator runs during compilation. No reflection, no dynamic code generation at runtime.

## Zero reflection at runtime

All mapping infrastructure is generated as compile-time constants:

- **Field names**: string constants in the generated `FieldNames` class
- **Mappings JSON**: raw string literals embedded in the assembly
- **Settings JSON**: raw string literals embedded in the assembly
- **Property maps**: pre-computed dictionaries
- **Content hashes**: pre-computed SHA256 hash strings

## How it works

Elastic.Mapping uses an `IIncrementalGenerator` (Roslyn's incremental source generator API) that runs at build time:

1. **Discovers** classes with `[ElasticsearchMappingContext]`
2. **Analyzes** registered types and their properties
3. **Reads** STJ configuration from `[JsonSourceGenerationOptions]`
4. **Generates** resolver classes with all metadata as constants
5. **Emits** JSON strings as raw string literals

The generated code has no dependency on `System.Reflection` for field resolution or mapping computation.

## Multi-targeting

The runtime library targets both `netstandard2.0` and `net8.0`:

- **netstandard2.0**: broad compatibility, uses polyfills for newer language features
- **net8.0**: uses built-in language features, static abstract interface members

The source generator itself targets `netstandard2.0` for Roslyn compatibility.

## Incremental builds

The `IIncrementalGenerator` API ensures the generator only re-runs when inputs change:

- Adding or removing a property triggers regeneration for that type
- Changing an attribute triggers regeneration for that type
- Unrelated code changes do not trigger regeneration

## Publishing with AOT

```shell
dotnet publish -p:PublishAot=true
```

When paired with STJ source generation, the full pipeline from domain types to Elasticsearch is AOT safe:

```csharp
// STJ source-generated serialization
[JsonSerializable(typeof(Product))]
public partial class MyJsonContext : JsonSerializerContext;

// Elastic.Mapping source-generated mappings
[ElasticsearchMappingContext(JsonContext = typeof(MyJsonContext))]
[Entity<Product>(Target = EntityTarget.Index, Name = "products")]
public static partial class MyContext;

// Elastic.Esql LINQ-to-ES|QL (expression tree visitor, no reflection)
var esql = new EsqlQueryable<Product>(MyContext.Instance)
    .Where(p => p.Price > 100)
    .ToString();
```

No reflection anywhere in the pipeline.
