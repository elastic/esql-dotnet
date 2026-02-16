# Elastic.Esql & Elastic.Mapping

Type-safe Elasticsearch development for .NET. Write LINQ, get [ES|QL](elasticsearch://reference/query-languages/esql.md). Define mappings in C#, get AOT-ready source-generated infrastructure.

## ES|QL LINQ

Write C# LINQ expressions, get ES|QL query strings. Two packages: `Elastic.Clients.Esql` for query execution against a cluster, and `Elastic.Esql` for translation only.

```csharp
var results = await client.Query<LogEntry>()
    .Where(l => l.Level == "ERROR" && l.Duration > 1000)
    .OrderByDescending(l => l.Timestamp)
    .Take(50)
    .ToListAsync();
```

Produces:

```
FROM logs-*
| WHERE (log.level.keyword == "ERROR" AND duration > 1000)
| SORT @timestamp DESC
| LIMIT 50
```

[Get started with ES|QL LINQ →](esql/index.md)

## Elastic.Mapping

A source generator that turns POCOs into type-safe, pre-computed Elasticsearch mapping infrastructure at build time. Zero reflection, zero runtime overhead, fully AOT compatible.

```csharp
[ElasticsearchMappingContext]
[Entity<Product>(Target = EntityTarget.Index, Name = "products")]
public static partial class MyContext;

// Type-safe field names - rename the property, these update automatically
MyContext.Product.Fields.Name   // "name"
MyContext.Product.Fields.Price  // "price"
```

[Get started with Elastic.Mapping →](mapping/index.md)

## Key features

- **Type-safe ES|QL**: write LINQ, get correct ES|QL with full IntelliSense and compile-time checking
- **Source-generated mappings**: field names, index settings, and mappings JSON computed at build time
- **AOT compatible**: zero reflection at runtime, works with `PublishAot=true`
- **System.Text.Json native**: inherits naming policies, enum handling, and ignore conditions from your STJ context
- **Hash-based change detection**: SHA256 hashes detect when mappings change, enabling schema drift detection

## Packages

| Package                | Description                                                       |
|------------------------|-------------------------------------------------------------------|
| `Elastic.Mapping`      | Source-generated Elasticsearch mappings (includes the generator)  |
| `Elastic.Esql`         | [LINQ-to-ES\|QL translation library](esql/package-translation.md) |
| `Elastic.Clients.Esql` | [ES\|QL execution via Elastic.Transport](esql/package-client.md)  |
