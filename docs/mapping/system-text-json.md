---
navigation_title: STJ integration
---

# System.Text.Json integration

Elastic.Mapping is built around `System.Text.Json`. Link your STJ source-generated `JsonSerializerContext` and the mapping generator inherits your serialization configuration automatically. One source of truth for both JSON serialization and Elasticsearch field names.

## Linking a JsonSerializerContext

Set `JsonContext` on the `[ElasticsearchMappingContext]` attribute:

```csharp
// Your STJ source-generated context
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Order))]
public partial class MyJsonContext : JsonSerializerContext;

// Link it to the mapping context
[ElasticsearchMappingContext(JsonContext = typeof(MyJsonContext))]
[Entity<Product>(Target = EntityTarget.Index, Name = "products")]
[Entity<Order>(Target = EntityTarget.Index, Name = "orders")]
public static partial class MyContext;
```

## What gets inherited

The generator reads `[JsonSourceGenerationOptions]` at compile time and applies:

| STJ option | Effect on mappings |
|---|---|
| `PropertyNamingPolicy` | Field names follow the same policy (`camelCase`, `snake_case_lower`, `kebab-case-lower`, etc.) |
| `UseStringEnumConverter` | Enum fields map to `keyword` instead of `integer` |
| `DefaultIgnoreCondition` | Ignored properties are excluded from mappings |
| `IgnoreReadOnlyProperties` | Read-only properties are excluded from mappings |

### Naming policy examples

| Policy | C# property | Elasticsearch field |
|---|---|---|
| `CamelCase` | `FirstName` | `firstName` |
| `SnakeCaseLower` | `FirstName` | `first_name` |
| `SnakeCaseUpper` | `FirstName` | `FIRST_NAME` |
| `KebabCaseLower` | `FirstName` | `first-name` |
| `KebabCaseUpper` | `FirstName` | `FIRST-NAME` |

## Per-property overrides

### JsonPropertyName

`[JsonPropertyName]` overrides the naming policy for individual properties:

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }  // → "@timestamp"

    [JsonPropertyName("log.level")]
    public string Level { get; set; }        // → "log.level"

    public string Message { get; set; }      // → "message" (from naming policy)
}
```

### JsonIgnore

`[JsonIgnore]` excludes properties from the generated mappings entirely:

```csharp
public class Product
{
    public string Name { get; set; }

    [JsonIgnore]
    public string InternalTrackingId { get; set; }  // Not in mappings
}
```

### Per-property enum handling

`[JsonConverter(typeof(JsonStringEnumConverter))]` on individual enum properties overrides the global enum setting:

```csharp
public class Order
{
    // Uses global setting (string or int depending on UseStringEnumConverter)
    public OrderStatus Status { get; set; }

    // Always serialized as string, regardless of global setting
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Priority Priority { get; set; }
}
```

## Without a linked context

When no `JsonContext` is specified, Elastic.Mapping still respects:

- `[JsonPropertyName]` attributes on properties
- `[JsonIgnore]` attributes on properties
- Default camelCase naming convention

The difference is that global `[JsonSourceGenerationOptions]` settings are not available, so the generator uses its own defaults.
