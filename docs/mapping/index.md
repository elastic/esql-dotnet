# Elastic.Mapping

Compile-time Elasticsearch mappings for .NET. Native AOT ready. Define your index mappings, analysis chains, and field metadata with C# attributes and get reflection-free generated code that works with `System.Text.Json` source generation out of the box.

## The problem

Elasticsearch field names are strings. Typos are silent. Refactors break queries. Manual JSON mappings drift from your code.

## The solution

Elastic.Mapping uses a source generator that turns POCOs into type-safe, pre-computed mapping infrastructure at build time. Zero reflection, zero runtime overhead, fully AOT compatible.

## Key features

- **Type-safe field names**: rename a C# property and field constants update automatically
- **Pre-computed JSON**: settings and mappings are embedded as string literals, ready to send to Elasticsearch
- **System.Text.Json native**: inherits naming policies, enum handling, and ignore conditions from your STJ context
- **Hash-based change detection**: SHA256 hashes detect when mappings change
- **AOT compatible**: zero reflection at runtime, works with `PublishAot=true`
- **Analysis builder**: fluent API for custom analyzers, tokenizers, and filters with source-generated name constants

## Install

```shell
dotnet add package Elastic.Mapping
```

The NuGet package includes both the runtime API and the source generator. No separate analyzer package needed.

## What gets generated

For each registered type, the source generator produces:

- **Field constants**: `MyContext.Product.Fields.Name` (compile-time safe field names)
- **Bidirectional field mapping**: `PropertyToField` / `FieldToProperty` dictionaries
- **Index/search strategy**: write targets, search patterns, data stream names
- **Settings + mappings JSON**: pre-computed, ready for index creation
- **Content hashes**: detect when mappings change
- **Analysis accessors**: type-safe constants for custom analyzers/filters
- **Mappings builder**: per-property fluent API for customization

[Get started →](getting-started.md)
