---
navigation_title: Getting started
---

# Getting started with Elastic.Mapping

## 1. Define your domain types

Domain types are clean POCOs. No base classes, no `partial` keyword. Use field type attributes to control how properties map to Elasticsearch field types:

```csharp
public class Product
{
    [Keyword]
    public string Id { get; set; }

    [Text(Analyzer = "standard")]
    public string Name { get; set; }

    public double Price { get; set; }

    public bool InStock { get; set; }

    [Nested]
    public List<Category> Categories { get; set; }
}
```

Properties without attributes are inferred from their CLR type. See [default type inference](#default-type-inference) below.

## 2. Register types in a mapping context

Create a `static partial class` with the `[ElasticsearchMappingContext]` attribute and register types with `[Index<T>]` or `[DataStream<T>]`:

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products", SearchPattern = "products*")]
[DataStream<ApplicationLog>(Type = "logs", Dataset = "myapp", Namespace = "production")]
public static partial class MyContext;
```

## 3. Use generated field constants and metadata

```csharp
// Type-safe field names - rename the C# property, these update automatically
MyContext.Product.Fields.Name      // "name"
MyContext.Product.Fields.Price     // "price"
MyContext.Product.Fields.InStock   // "inStock"

// Index targets
MyContext.Product.IndexStrategy.WriteTarget   // "products"
MyContext.Product.SearchStrategy.Pattern      // "products*"

// Data stream naming follows Elastic conventions
MyContext.ApplicationLog.IndexStrategy.DataStreamName  // "logs-myapp-production"

// Pre-built JSON for index creation
var json = MyContext.Product.Context.GetIndexJson();

// Change detection - only update when mappings actually change
if (clusterHash != MyContext.Product.Hash)
    UpdateMappings();
```

## Field type attributes

| Attribute | Elasticsearch type | Use case |
|---|---|---|
| `[Text]` | `text` | Full-text search, analyzers |
| `[Keyword]` | `keyword` | Exact match, aggregations, sorting |
| `[Date]` | `date` | Timestamps, date math |
| `[Long]` | `long` | 64-bit integer |
| `[Double]` | `double` | Double-precision floating point |
| `[Boolean]` | `boolean` | True/false |
| `[Object]` | `object` | Flattened object fields |
| `[Nested]` | `nested` | Preserve array element relationships |
| `[GeoPoint]` | `geo_point` | Latitude/longitude |
| `[GeoShape]` | `geo_shape` | Complex geographic shapes |
| `[Ip]` | `ip` | IPv4/IPv6 addresses |
| `[Completion]` | `completion` | Autocomplete suggestions |
| `[DenseVector(Dims = 384)]` | `dense_vector` | Embeddings, kNN search |
| `[SemanticText]` | `semantic_text` | ELSER / semantic search |

## Default type inference

Properties without explicit attributes are inferred from their CLR type:

| C# type | Elasticsearch type |
|---|---|
| `string` | `text` (with `.keyword` sub-field) |
| `int` | `integer` |
| `long` | `long` |
| `float` | `float` |
| `double` | `double` |
| `decimal` | `double` |
| `bool` | `boolean` |
| `DateTime`, `DateTimeOffset` | `date` |
| `byte[]` | `binary` |
| Collections of objects | `nested` |
| Enum | `keyword` (string) or `integer` (numeric) |

## Index strategies

### Traditional index

```csharp
[Index<Product>(
    Name = "products",
    WriteAlias = "products-write",
    ReadAlias = "products-read",
    SearchPattern = "products*",
    Shards = 3,
    RefreshInterval = "5s"
)]
```

### Rolling date index

```csharp
[Index<Order>(Name = "orders", DatePattern = "yyyy.MM")]
// Write target: orders-2025.02
// Search pattern: orders-*
```

### Data stream

```csharp
[DataStream<ApplicationLog>(Type = "logs", Dataset = "ecommerce.app", Namespace = "production")]
// Data stream: logs-ecommerce.app-production
// Search pattern: logs-ecommerce.app-*
```

## Linking with Elastic.Esql

Pass the generated context to `EsqlQueryable` for AOT-safe LINQ-to-ES|QL translation:

```csharp
var esql = new EsqlQueryable<Product>(MyContext.Instance)
    .Where(p => p.Name.Contains("laptop"))
    .OrderByDescending(p => p.Price)
    .Take(10)
    .ToString();
```
