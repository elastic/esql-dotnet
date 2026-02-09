---
navigation_title: Analysis
---

# Analysis configuration

Build custom analyzers, tokenizers, and filters with a fluent API. The source generator creates type-safe constants for your custom analysis components.

## ConfigureAnalysis

Define a static `ConfigureAnalysis` method in a configuration class and reference it from the index attribute:

```csharp
public static class ProductConfig
{
    public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
        .Analyzer("product_search", a => a.Custom()
            .Tokenizer(BuiltIn.Tokenizers.Standard)
            .Filter(BuiltIn.TokenFilters.Lowercase, "english_stemmer", "edge_ngram_3_8"))
        .TokenFilter("english_stemmer", f => f.Stemmer()
            .Language(BuiltIn.StemmerLanguages.English))
        .TokenFilter("edge_ngram_3_8", f => f.EdgeNGram()
            .MinGram(3).MaxGram(8));
}

[Index<Product>(Name = "products", Configuration = typeof(ProductConfig))]
```

## AnalysisBuilder API

The `AnalysisBuilder` provides methods for each analysis component type:

```csharp
analysis
    .Analyzer(name, configure)      // Custom analyzers
    .Tokenizer(name, configure)     // Custom tokenizers
    .TokenFilter(name, configure)   // Custom token filters
    .CharFilter(name, configure)    // Custom character filters
    .Normalizer(name, configure)    // Custom normalizers
```

### Analyzer types

| Builder method | Description |
|---|---|
| `.Custom()` | Custom analyzer with configurable tokenizer and filters |
| `.Standard()` | Standard analyzer |
| `.Simple()` | Simple analyzer |
| `.Whitespace()` | Whitespace analyzer |
| `.Keyword()` | No tokenization |
| `.Pattern()` | Pattern-based analyzer |
| `.Language(lang)` | Language-specific analyzer |
| `.Fingerprint()` | Fingerprint analyzer |

### Example: custom analyzer

```csharp
analysis.Analyzer("my_analyzer", a => a.Custom()
    .Tokenizer(BuiltIn.Tokenizers.Standard)
    .Filter(
        BuiltIn.TokenFilters.Lowercase,
        BuiltIn.TokenFilters.AsciiFolding,
        "my_stop_filter"
    )
    .CharFilter(BuiltIn.CharFilters.HtmlStrip))
```

### Example: token filter

```csharp
analysis
    .TokenFilter("my_stop_filter", f => f.Stop()
        .Stopwords(BuiltIn.StopWords.English))
    .TokenFilter("my_shingle", f => f.Shingle()
        .MinShingleSize(2)
        .MaxShingleSize(3))
    .TokenFilter("my_edge_ngram", f => f.EdgeNGram()
        .MinGram(2)
        .MaxGram(10))
```

### Example: normalizer

```csharp
analysis.Normalizer("my_normalizer", n => n.Custom()
    .Filter(BuiltIn.TokenFilters.Lowercase, BuiltIn.TokenFilters.AsciiFolding))
```

## Generated analysis constants

The source generator parses your `ConfigureAnalysis` method and generates a typed class with constants for each component:

```csharp
// After defining analyzers and filters in ConfigureAnalysis:
MyContext.Product.Analysis.Analyzers.ProductSearch       // "product_search"
MyContext.Product.Analysis.TokenFilters.EnglishStemmer    // "english_stemmer"
MyContext.Product.Analysis.TokenFilters.EdgeNgram38       // "edge_ngram_3_8"
```

These constants can be used in [mappings configuration](mappings-configuration.md) to reference your custom analyzers by name.

## Built-in analysis components

The `BuiltIn` class provides constants for all Elasticsearch built-in analysis components:

### Analyzers

`BuiltIn.Analyzers.Standard`, `Simple`, `Whitespace`, `Stop`, `Keyword`, `Pattern`, `Fingerprint`, and 40+ language-specific analyzers.

### Tokenizers

`BuiltIn.Tokenizers.Standard`, `Letter`, `Lowercase`, `Whitespace`, `UaxUrlEmail`, `Classic`, `Thai`, `NGram`, `EdgeNGram`, `Keyword`, `Pattern`, `SimplePattern`, `CharGroup`, `PathHierarchy`.

### Token filters

60+ built-in token filters including `Lowercase`, `Uppercase`, `AsciiFolding`, `Stop`, `Stemmer`, `Synonym`, `Shingle`, `EdgeNGram`, `NGram`, `Truncate`, `Unique`, `Length`, `Reverse`, and language-specific filters.

### Character filters

`BuiltIn.CharFilters.HtmlStrip`, `Mapping`, `PatternReplace`, `IcuNormalizer`.

### Stop words and stemmer languages

Pre-defined constants for 25+ languages of stop words and 40+ stemmer language variants.
