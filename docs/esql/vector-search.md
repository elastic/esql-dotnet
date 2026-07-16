---
navigation_title: Vector & hybrid search
---

# ES|QL vector and hybrid search

ES|QL supports first-class [dense vector search](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions.md) and [hybrid search](elasticsearch://reference/query-languages/esql/commands/fuse.md) (combining lexical and semantic results). Elastic.Esql exposes the full surface area through:

- `DenseVector<T>` (with `T = float` or `T = byte`) for every `dense_vector` field and parameter, with implicit conversions from `T[]` and `ReadOnlyMemory<T>`
- `EsqlFunctions.Knn`, `EsqlFunctions.TextEmbedding`, and `V_*` similarity functions
- `MetadataField` flags + `EsqlMetadata` static markers for `_id` / `_score` / `_source` / etc.
- `Fork(...)` + `Fuse(...)` extension methods for hybrid search

## Vector type

Vectors are represented as `DenseVector<T>` everywhere — model properties, function parameters, and `TextEmbedding` return values. Two element types are supported:

- `DenseVector<float>` for `dense_vector` fields with `element_type: "float"`.
- `DenseVector<byte>` for both `element_type: "byte"` and `element_type: "bit"`. The wire format is identical (a JSON array of signed bytes); the bundled JSON converter handles the signed-byte encoding so callers pass natural unsigned `byte` values.

Implicit conversions let you pass `T[]` or `ReadOnlyMemory<T>` directly:

```csharp
DenseVector<float> queryVec = new float[] { 0.5f, 0.25f, 0.75f };  // float[] -> DenseVector<float>
DenseVector<byte>  rgbRed   = new byte[]  { 255, 0, 0 };           // byte[]  -> DenseVector<byte>
```

For bit vectors, the user is responsible for the bit-packing semantics: 8 bits per byte, so a `dims: 16` bit vector is a `DenseVector<byte>` of length 2.

```csharp
DenseVector<byte> bit16 = new byte[] { 0xFF, 0x00 };  // 16-dim bit vector (16 bits packed into 2 bytes)
```

## Approximate KNN search

The [`KNN` function](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/knn.md) finds the k nearest vectors to a query vector via approximate search. Use it inside `Where(...)`:

```csharp
public class Book
{
    public string Title { get; set; } = "";
    public DenseVector<float> Embedding { get; set; }
}

var queryVec = new float[] { 0.5f, 0.25f, 0.75f /* ... */ };

var results = await client.CreateQuery<Book>()
    .From("books", MetadataField.Score)
    .Where(b => EsqlFunctions.Knn(b.Embedding, queryVec))
    .OrderByDescending(_ => EsqlMetadata.Score)
    .Take(10)
    .AsEsqlQueryable()
    .ToListAsync();
```

```esql
FROM books METADATA _score
| WHERE KNN(embedding, [0.5, 0.25, 0.75, ...])
| SORT _score DESC
| LIMIT 10
```

The `LIMIT` automatically becomes the per-shard `k`. Any other `Where(...)` clauses become prefilters that ES|QL evaluates before the KNN search -- you don't need a separate `prefilter` parameter.

### KNN options

For fine-grained control, pass a typed `KnnOptions` record as the third argument. Each set property maps to an [ES|QL named parameter](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/knn.md):

```csharp
.Where(b => EsqlFunctions.Knn(b.Embedding, queryVec, new KnnOptions
{
    K = 10,
    MinCandidates = 100,
    Similarity = 0.5,
    Boost = 1.5
}))
```

```esql
| WHERE KNN(embedding, [...], { "k": 10, "min_candidates": 100, "similarity": 0.5, "boost": 1.5 })
```

Available options on `KnnOptions`: `K`, `MinCandidates`, `Similarity`, `Boost`, `VisitPercentage`, `RescoreOversample`. See the [KNN function reference](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/knn.md) for accepted values.

### KNN over byte and bit vectors

`KNN` also works with `dense_vector` fields whose element type is `byte` or `bit`. Use `DenseVector<byte>` and pass natural unsigned `byte` values; the JSON converter renders the signed-byte wire format expected by ES|QL.

```csharp
public class Color
{
    public string Name { get; set; } = "";
    public DenseVector<byte> RgbVector { get; set; }   // dense_vector { element_type: "byte", dims: 3 }
}

await client.CreateQuery<Color>()
    .From("colors", MetadataField.Score)
    .Where(c => EsqlFunctions.Knn(c.RgbVector, new byte[] { 0, 120, 0 }))
    .OrderByDescending(_ => EsqlMetadata.Score)
    .Take(5)
    .AsEsqlQueryable()
    .ToListAsync();
```

## Semantic search with `TEXT_EMBEDDING`

Generate the query vector at query time from a text input using a configured inference endpoint. Chain it directly into `KNN`:

```csharp
.Where(b => EsqlFunctions.Knn(
    b.Embedding,
    EsqlFunctions.TextEmbedding("vegan recipes", "my-embedding-endpoint")))
```

```esql
| WHERE KNN(embedding, TEXT_EMBEDDING("vegan recipes", "my-embedding-endpoint"))
```

The `InferenceEndpoints.TextEmbedding` static class exposes well-known serverless endpoint IDs:

| Constant | Value |
|---|---|
| `InferenceEndpoints.TextEmbedding.ElserV2` | `.elser-v2-elasticsearch` |
| `InferenceEndpoints.TextEmbedding.MultilingualE5Small` | `.multilingual-e5-small-elasticsearch` |

You can pass any string for custom endpoints.

## Exact similarity functions

For small datasets or after restrictive prefilters, use the `V_*` functions for exact similarity computation on retrieved rows. They live on `EsqlFunctions` and return `double`, so they can be used in `Where`, `Select`, and `OrderBy`:

```csharp
public class Color
{
    public string Name { get; set; } = "";
    public DenseVector<float> Embedding { get; set; }
}

await client.CreateQuery<Color>()
    .From("colors")
    .Where(c => c.Name != "black")
    .Select(c => new
    {
        c.Name,
        Similarity = EsqlFunctions.VCosine(c.Embedding, new float[] { 0f, 1f, 1f })
    })
    .OrderByDescending(c => c.Similarity)
    .Take(10)
    .AsEsqlQueryable()
    .ToListAsync();
```

```esql
FROM colors
| WHERE name != "black"
| EVAL similarity = V_COSINE(embedding, [0, 1, 1])
| SORT similarity DESC
| KEEP name, similarity
| LIMIT 10
```

| ES\|QL | `EsqlFunctions` | Notes |
|---|---|---|
| [`V_COSINE`](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/v_cosine.md) | `EsqlFunctions.VCosine(a, b)` | Cosine similarity (float vectors) |
| [`V_DOT_PRODUCT`](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/v_dot_product.md) | `EsqlFunctions.VDotProduct(a, b)` | Dot product (float vectors) |
| [`V_HAMMING`](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/v_hamming.md) | `EsqlFunctions.VHamming(a, b)` | Hamming distance (byte vectors) |
| [`V_L1_NORM`](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/v_l1_norm.md) | `EsqlFunctions.VL1Norm(a, b)` | L1 (Manhattan) norm (float vectors) |
| [`V_L2_NORM`](elasticsearch://reference/query-languages/esql/functions-operators/dense-vector-functions/v_l2_norm.md) | `EsqlFunctions.VL2Norm(a, b)` | L2 (Euclidean) norm (float vectors) |

## Document metadata

ES|QL exposes [document metadata fields](elasticsearch://reference/query-languages/esql/esql-metadata-fields.md) (`_id`, `_score`, `_source`, ...) via the `METADATA` directive on `FROM`. Elastic.Esql models this with two complementary types:

- **`MetadataField`** -- a `[Flags]` enum that selects which fields to request
- **`EsqlMetadata`** -- a static marker class that exposes each field for use inside lambda expressions

### Requesting metadata

Pass a `MetadataField` value (or combination via `|`) to `From`:

```csharp
.From("books", MetadataField.Id | MetadataField.Score | MetadataField.Index)
```

```esql
FROM books METADATA _id, _index, _score
```

| Flag | ES\|QL field | Type |
|---|---|---|
| `MetadataField.Id` | `_id` | `keyword` |
| `MetadataField.Ignored` | `_ignored` | `keyword[]` |
| `MetadataField.Index` | `_index` | `keyword` |
| `MetadataField.IndexMode` | `_index_mode` | `keyword` |
| `MetadataField.Score` | `_score` | `float` |
| `MetadataField.Size` | `_size` | `integer` |
| `MetadataField.Source` | `_source` | special `_source` type |
| `MetadataField.Version` | `_version` | `long` |
| `MetadataField.All` | all of the above | -- |

### Using the `EsqlMetadata` marker

`EsqlMetadata.X` is a marker member that emits the corresponding underscore-prefixed identifier when used in a `Where`, `OrderBy`, `Select`, or `Fuse` lambda. The translator validates that the matching `MetadataField` was requested on `From` and throws a clear error otherwise.

```csharp
await client.CreateQuery<Book>()
    .From("books", MetadataField.Id | MetadataField.Score)
    .Where(b => EsqlFunctions.Match(b.Title, "Shakespeare"))
    .OrderByDescending(_ => EsqlMetadata.Score)         // SORT _score DESC
    .Select(b => new
    {
        Id = EsqlMetadata.Id,                            // RENAME _id AS id
        Score = EsqlMetadata.Score,                      // RENAME _score AS score
        b.Title
    })
    .Take(10)
    .AsEsqlQueryable()
    .ToListAsync();
```

```esql
FROM books METADATA _id, _score
| WHERE MATCH(title, "Shakespeare")
| SORT _score DESC
| RENAME _id AS id, _score AS score
| KEEP title, id, score
| LIMIT 10
```

`EsqlMetadata.Source` returns a `JsonObject`. Use `EsqlMetadata.SourceAs<T>()` to project `_source` directly into a typed destination:

```csharp
.Select(b => new { Original = EsqlMetadata.SourceAs<Book>(), Score = EsqlMetadata.Score })
```

`EsqlMetadata.Fork` references the `_fork` discriminator added by [FORK](#fork--parallel-pipelines).

### Auto-retention through projections

Active metadata is automatically retained in any `KEEP` emitted by a `Select` or `LookupJoin` projection, unless the projection itself consumes the field via an `EsqlMetadata.X` rename:

```csharp
.From("logs-*", MetadataField.Score | MetadataField.Id)
.Select(l => new { l.Message })
```

```esql
FROM logs-* METADATA _id, _score
| KEEP message, _id, _score
```

Metadata is **cleared** by aggregations (`Sum`, `Count`, `GroupBy`-driven `STATS`, etc.) -- mirroring ES|QL semantics where metadata is no longer accessible after `STATS`.

### LOOKUP JOIN and metadata

`LOOKUP JOIN` only allows fields from the lookup index to shadow regular outer fields with the same name (per [the spec](elasticsearch://reference/query-languages/esql/commands/lookup-join.md)). Metadata is **shadow-proof in practice** because (1) `METADATA` is only valid on `FROM`, never on `LOOKUP JOIN`, and (2) the `_*` namespace is reserved by Elasticsearch so a lookup index mapping cannot define a real field literally named `_id` / `_score` / etc. Source-side metadata always survives a join unchanged, and `EsqlMetadata.X` markers continue to resolve to the source document's metadata after a `LookupJoin`.

## Hybrid search: FORK + FUSE

[`FORK`](elasticsearch://reference/query-languages/esql/commands/fork.md) runs multiple parallel pipeline branches over the same input. [`FUSE`](elasticsearch://reference/query-languages/esql/commands/fuse.md) merges the resulting rows by key and re-scores them using either Reciprocal Rank Fusion (RRF) or linear combination. The combination enables **hybrid search**: combine lexical (`MATCH`) and semantic (`KNN`) results into a single ranked list.

### `Fork(params lambda branches)`

```csharp
public static IQueryable<T> Fork<T>(
    this IQueryable<T> source,
    params Expression<Func<IQueryable<T>, IQueryable<T>>>[] branches);
```

Up to 8 lambda branches. Each branch operates on a fresh `IQueryable<T>` and is translated through the same machinery as the main pipeline. ES|QL automatically names branches `fork1`, `fork2`, ..., `forkN` in declaration order -- there is no language syntax to rename them, so the C# API doesn't expose a renaming knob either.

### `Fuse(...)`

```csharp
public static IQueryable<T> Fuse<T>(
    this IQueryable<T> source,
    FuseMethod method = FuseMethod.Rrf,
    int? rankConstant = null,                             // RRF only
    ScoreNormalizer normalizer = ScoreNormalizer.None,    // Linear only
    double[]? weights = null,                             // ordered by branch index
    Expression<Func<T, object?>>? score = null,           // default => _score
    Expression<Func<T, object?>>? group = null,           // default => _fork
    Expression<Func<T, object?>>? key = null);            // default => _id, _index
```

All parameters have sensible defaults. The most common call is just `.Fuse()`.

- **`weights`** is a positional `double[]` aligned to fork declaration order -- no magic `"fork1"` / `"fork2"` strings. The translator validates `weights.Length == branchCount` at translation time.
- **`score` / `group` / `key`** are optional lambdas accepting either a regular field on `T`, an `EsqlMetadata.X` marker, or (for `key`) an anonymous-type composite (`x => new { x.Id, x.Index }`).
- When the lambdas are omitted, no `SCORE BY` / `GROUP BY` / `KEY BY` clause is generated -- ES|QL's defaults of `_score` / `_fork` / `_id, _index` apply.

### Hybrid lexical + semantic search example

```csharp
public class Book
{
    public string Title { get; set; } = "";
    public DenseVector<float> TitleVec { get; set; }
}

var queryVec = await GetEmbeddingAsync("vegetarian curry");

var results = await client.CreateQuery<Book>()
    .From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
    .Fork(
        b => b.Where(x => EsqlFunctions.Match(x.Title, "vegetarian curry"))
              .OrderByDescending(_ => EsqlMetadata.Score)
              .Take(50),
        b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, queryVec))
              .Take(50))
    .Fuse()                                // RRF (default), rank constant 60
    .OrderByDescending(_ => EsqlMetadata.Score)
    .Take(10)
    .AsEsqlQueryable()
    .ToListAsync();
```

```esql
FROM books METADATA _id, _index, _score
| FORK (WHERE MATCH(title, "vegetarian curry") | SORT _score DESC | LIMIT 50) (WHERE KNN(titleVec, [...]) | LIMIT 50)
| FUSE
| SORT _score DESC
| LIMIT 10
```

### Linear combination with weights

```csharp
.Fuse(
    method: FuseMethod.Linear,
    normalizer: ScoreNormalizer.MinMax,
    weights: [0.7, 0.3])
```

```esql
| FUSE LINEAR WITH { "normalizer": "minmax", "weights": { "fork1": 0.7, "fork2": 0.3 } }
```

`weights` are aligned positionally to the preceding `Fork`'s branch declaration order. The first weight applies to the first branch, the second to the second, and so on. Mismatched lengths throw `ArgumentException` at translation time.

### Custom RRF rank constant

```csharp
.Fuse(rankConstant: 80)
```

```esql
| FUSE WITH { "rank_constant": 80 }
```

### Custom score / group / key columns

The defaults (`_score`, `_fork`, `_id`, `_index`) cover almost every use case, but you can override them when needed:

```csharp
.Fuse(
    score: x => x.MyCustomScore,                      // SCORE BY myCustomScore
    key: x => new { x.Id, x.Index })                  // KEY BY id, index
```

### Validation

`Fuse(...)` validates at translation time:

- It must immediately follow `Fork(...)` -- otherwise `InvalidOperationException`.
- `weights.Length` must match the preceding `Fork` branch count -- otherwise `ArgumentException`.
- Any `EsqlMetadata.X` referenced in a lambda must be in scope -- otherwise `InvalidOperationException` naming both the marker and the missing `MetadataField`.

## Custom scoring

Combine all of the above to build custom scoring expressions:

```csharp
.From("books", MetadataField.Score)
.Where(b => EsqlFunctions.Knn(b.TitleVec, queryVec))
.Select(b => new
{
    b.Title,
    Score = EsqlMetadata.Score,
    AdjustedScore = EsqlMetadata.Score * 2 + EsqlFunctions.VCosine(b.TitleVec, queryVec)
})
.OrderByDescending(x => x.AdjustedScore)
.Take(10);
```

```esql
FROM books METADATA _score
| WHERE KNN(titleVec, [...])
| RENAME _score AS score
| EVAL adjustedScore = ((_score * 2) + V_COSINE(titleVec, [...]))
| KEEP title, score, adjustedScore
| SORT adjustedScore DESC
| LIMIT 10
```

## API reference

### Marker methods on `EsqlFunctions`

All `dense_vector` parameters use `DenseVector<T>`. `Knn<T>` is generic over the element type; `V_*` and `TextEmbedding` constrain `T` to the type that ES|QL accepts. Implicit conversions from `T[]` and `ReadOnlyMemory<T>` cover the common call sites.

| Method | ES\|QL |
|---|---|
| `Knn<T>(DenseVector<T> field, DenseVector<T> query)` | `KNN(field, query)` |
| `Knn<T>(DenseVector<T> field, DenseVector<T> query, KnnOptions options)` | `KNN(field, query, { ... })` |
| `TextEmbedding(string text, string inferenceId)` | `TEXT_EMBEDDING("text", "id")` |
| `VCosine(DenseVector<float> a, DenseVector<float> b)` | `V_COSINE(a, b)` |
| `VDotProduct(DenseVector<float> a, DenseVector<float> b)` | `V_DOT_PRODUCT(a, b)` |
| `VHamming(DenseVector<byte> a, DenseVector<byte> b)` | `V_HAMMING(a, b)` (byte vectors only) |
| `VL1Norm(DenseVector<float> a, DenseVector<float> b)` | `V_L1_NORM(a, b)` |
| `VL2Norm(DenseVector<float> a, DenseVector<float> b)` | `V_L2_NORM(a, b)` |

### Extension methods

| Method | Description |
|---|---|
| `.From(string indexPattern, MetadataField metadata)` | Source command with optional `METADATA` directive |
| `.Fork(params Expression<Func<IQueryable<T>, IQueryable<T>>>[])` | Up to 8 parallel pipeline branches |
| `.Fuse()` / `.Fuse(method, rankConstant, normalizer, weights, score, group, key)` | Merge FORK results with RRF or linear combination |
