---
navigation_title: Field configuration
---

# Mappings configuration

The source generator creates a typed mappings builder for each registered type. Use it to customize field definitions, add runtime fields, and define dynamic templates.

## ConfigureMappings

Define a static `ConfigureMappings` method alongside `ConfigureAnalysis` in your configuration class:

```csharp
public static class ProductConfig
{
    public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
        .Analyzer("product_search", a => a.Custom()
            .Tokenizer(BuiltIn.Tokenizers.Standard)
            .Filter(BuiltIn.TokenFilters.Lowercase));

    public static ProductMappingsBuilder ConfigureMappings(ProductMappingsBuilder m) => m
        .Name(f => f.Analyzer("product_search")
            .MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)))
        .Price(f => f.DocValues(true));
}

[Entity<Product>(Target = EntityTarget.Index, Name = "products", Configuration = typeof(ProductConfig))]
```

The `ProductMappingsBuilder` is generated with a method for each property on the `Product` type (e.g., `.Name()`, `.Price()`, `.InStock()`), providing full IntelliSense.

## Field builder

Each property method accepts a `FieldBuilder` that lets you select the field type and configure it:

```csharp
m.Name(f => f.Text()
    .Analyzer("product_search")
    .SearchAnalyzer("standard")
    .MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)))
```

### Available field types

The `FieldBuilder` supports all Elasticsearch field types:

| Method | Type | Key options |
|---|---|---|
| `.Text()` | `text` | `Analyzer`, `SearchAnalyzer`, `Norms`, `CopyTo`, `MultiField` |
| `.Keyword()` | `keyword` | `Normalizer`, `IgnoreAbove`, `DocValues` |
| `.Date()` | `date` | `Format`, `DocValues` |
| `.Long()` | `long` | `DocValues` |
| `.Integer()` | `integer` | `DocValues` |
| `.Double()` | `double` | `DocValues` |
| `.Float()` | `float` | `DocValues` |
| `.Boolean()` | `boolean` | `DocValues` |
| `.Ip()` | `ip` | `DocValues` |
| `.GeoPoint()` | `geo_point` | |
| `.GeoShape()` | `geo_shape` | |
| `.Nested()` | `nested` | `IncludeInParent`, `IncludeInRoot` |
| `.Object()` | `object` | `Enabled` |
| `.Completion()` | `completion` | `Analyzer`, `SearchAnalyzer` |
| `.DenseVector()` | `dense_vector` | `Dims`, `Similarity` |
| `.SemanticText()` | `semantic_text` | `InferenceId` |
| `.SearchAsYouType()` | `search_as_you_type` | |
| `.Flattened()` | `flattened` | |
| `.Binary()` | `binary` | |
| `.Alias(path)` | `alias` | Target field path |

## Multi-fields

Add sub-fields to a text or keyword field using `.MultiField()`:

```csharp
m.Name(f => f.Text()
    .Analyzer("standard")
    .MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256))
    .MultiField("autocomplete", mf => mf.SearchAsYouType()))
```

## Runtime fields

Add computed fields that are evaluated at query time using Painless scripts:

```csharp
m.AddRuntimeField("discount_pct", r => r.Double()
    .Script("emit((doc['price'].value - doc['sale_price'].value) / doc['price'].value * 100)"))
```

### Runtime field types

| Method | Type |
|---|---|
| `.Keyword()` | `keyword` |
| `.Long()` | `long` |
| `.Double()` | `double` |
| `.Date()` | `date` |
| `.Boolean()` | `boolean` |
| `.Ip()` | `ip` |
| `.GeoPoint()` | `geo_point` |

## Dynamic templates

Define rules for dynamically mapped fields:

```csharp
m.AddDynamicTemplate("labels_as_keyword", dt => dt
    .PathMatch("labels.*")
    .Mapping(mapping => mapping.Keyword()))

m.AddDynamicTemplate("strings_as_text", dt => dt
    .MatchMappingType("string")
    .Mapping(mapping => mapping.Text()
        .Analyzer("standard")))
```

### Dynamic template options

| Method | Description |
|---|---|
| `.Match(pattern)` | Field name wildcard pattern |
| `.Unmatch(pattern)` | Exclude fields matching this pattern |
| `.PathMatch(pattern)` | Dot-notation path pattern |
| `.PathUnmatch(pattern)` | Exclude paths matching this pattern |
| `.MatchMappingType(type)` | Match by detected JSON type (`string`, `long`, etc.) |
| `.MatchPattern("regex")` | Use regex matching instead of wildcards |
| `.Mapping(configure)` | Field definition to apply |

## Using analysis names in mappings

Reference your custom analyzers and normalizers from [analysis configuration](analysis.md) by name:

```csharp
public static class ProductConfig
{
    public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
        .Analyzer("product_search", a => a.Custom()
            .Tokenizer(BuiltIn.Tokenizers.Standard)
            .Filter(BuiltIn.TokenFilters.Lowercase))
        .Normalizer("lowercase_normalizer", n => n.Custom()
            .Filter(BuiltIn.TokenFilters.Lowercase));

    public static ProductMappingsBuilder ConfigureMappings(ProductMappingsBuilder m) => m
        .Name(f => f.Analyzer("product_search"))
        .Brand(f => f.Keyword().Normalizer("lowercase_normalizer"));
}
```

After generation, you can also use the generated constants:

```csharp
MyContext.Product.Analysis.Analyzers.ProductSearch           // "product_search"
MyContext.Product.Analysis.Normalizers.LowercaseNormalizer   // "lowercase_normalizer"
```

## Adding arbitrary fields

Add fields that don't correspond to C# properties:

```csharp
m.AddField("custom_score", f => f.Double().DocValues(true))
```
