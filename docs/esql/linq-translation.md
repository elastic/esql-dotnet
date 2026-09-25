---
navigation_title: LINQ translation
---

# LINQ to ES|QL translation

How LINQ operators map to [ES|QL commands](elasticsearch://reference/query-languages/esql.md). Elastic.Esql uses expression visitors to translate LINQ method chains into the ES|QL pipe-delimited command syntax.

## FROM - source resolution

Every query starts with `FROM`. The source index pattern is specified using the `.From()` extension method:

```csharp
var query = new EsqlQueryable<LogEntry>()
    .From("logs-*")
    .Where(l => l.Level == "ERROR")
    .ToString();
```

```
FROM logs-*
| WHERE log.level == "ERROR"
```

The `.From()` method accepts any index pattern string:

```csharp
.From("logs-*")           // FROM logs-*
.From("products")         // FROM products
.From("orders-2025.*")    // FROM orders-2025.*
```

If `.From()` is not called, the type name is used as the default index pattern with camelCase convention (e.g., `LogEntry` becomes `FROM logEntry`).

### Requesting metadata fields

Pass a `MetadataField` flag value to request ES|QL [document metadata fields](elasticsearch://reference/query-languages/esql/esql-metadata-fields.md) via the `METADATA` directive:

```csharp
.From("books", MetadataField.Id | MetadataField.Score | MetadataField.Index)
```

```
FROM books METADATA _id, _index, _score
```

Use the `EsqlMetadata` static marker class to reference these fields inside lambdas (`Where`, `OrderBy`, `Select`, `Fuse`):

```csharp
.From("books", MetadataField.Score)
.OrderByDescending(_ => EsqlMetadata.Score)
.Take(10)
// FROM books METADATA _score | SORT _score DESC | LIMIT 10
```

See the [vector and hybrid search guide](vector-search.md#document-metadata) for the full pattern, including `SourceAs<T>` typed `_source` projection and auto-retention through subsequent `KEEP` commands.

## Field name resolution

Field names are resolved from your C# type using `System.Text.Json` conventions:

- `[JsonPropertyName("@timestamp")]` on a property produces `@timestamp` in the query
- Properties without `[JsonPropertyName]` are resolved using the configured `JsonNamingPolicy` (defaults to camelCase)

```csharp
public class LogEntry
{
    [JsonPropertyName("@timestamp")]
    public DateTime Timestamp { get; set; }  // → @timestamp

    [JsonPropertyName("log.level")]
    public string Level { get; set; }        // → log.level

    public string Message { get; set; }      // → message
    public int StatusCode { get; set; }      // → statusCode
}
```

## WHERE - filtering

`.Where()` translates to the ES|QL [`WHERE`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-where) command. Multiple `.Where()` calls produce multiple `WHERE` commands:

```csharp
query
    .Where(l => l.StatusCode >= 500)
    .Where(l => l.Level == "ERROR")
```

```
FROM logs-*
| WHERE statusCode >= 500
| WHERE log.level == "ERROR"
```

### Null handling

```csharp
.Where(l => l.Tag == null)             // WHERE tag IS NULL
.Where(l => l.Tag != null)             // WHERE tag IS NOT NULL
.Where(l => l != null && l.Tag != null) // WHERE (true AND tag IS NOT NULL)
```

A null check on the document itself, which generated predicates often carry, is a constant: a document is never null. After a `Select` the parameter stands for the projected value, which can be null, so there such a check is refused rather than folded.

### Compound conditions

```csharp
.Where(l => l.Level == "ERROR" && l.Duration > 1000)
// WHERE (log.level == "ERROR" AND duration > 1000)

.Where(l => l.Level == "ERROR" || l.Level == "FATAL")
// WHERE (log.level == "ERROR" OR log.level == "FATAL")

.Where(l => !(l.StatusCode >= 500))
// WHERE NOT (statusCode >= 500)
```

### IN operator

```csharp
var levels = new[] { "ERROR", "FATAL", "CRITICAL" };
query.Where(l => levels.Contains(l.Level))
// WHERE log.level IN ("ERROR", "FATAL", "CRITICAL")
```

A `Contains` that takes an equality comparer is refused: the comparison is the one Elasticsearch performs, which the comparer would not follow.

### Boolean fields

```csharp
.Where(l => l.IsError)                 // WHERE isError
.Where(l => !l.IsError)                // WHERE NOT isError
```

### String methods

```csharp
.Where(l => l.Message.Contains("timeout"))    // WHERE message LIKE "*timeout*"
.Where(l => l.Host.StartsWith("prod-"))       // WHERE host LIKE "prod-*"
.Where(l => l.Path.EndsWith(".json"))          // WHERE path LIKE "*.json"
.Where(l => string.IsNullOrEmpty(l.Tag))       // WHERE (tag IS NULL OR tag == "")
```

### String ordering

`string.CompareOrdinal(a, b)` and `string.Compare(a, b, StringComparison.Ordinal)` compared with zero translate to the relational operator, which is what keyset pagination over a string key needs. A field that can be missing has its side of the ordering spelled out, since .NET orders null first where ES|QL would drop the row.

```csharp
.Where(l => string.CompareOrdinal(l.Message, "m") > 0)                       // WHERE message > "m"
.Where(l => string.Compare(l.ClientIp, "m", StringComparison.Ordinal) < 0)   // WHERE (clientIp IS NULL OR clientIp < "m")
```

`CompareTo` and the two-argument `Compare` order by the current culture and are refused. The value compared against must hold no surrogate pair and no character at or above U+E000, the only range where the UTF-16 ordering of .NET and the UTF-8 ordering of Elasticsearch disagree; otherwise the comparison is refused rather than answered with the wrong order.

Four more shapes are refused: two fields compared with each other, which leaves no value to look at; an expression of a field that can be missing, whose value for a missing field is not the field's null; a projected row, which has no field name of its own; and a property with a `JsonConverter`, whose field holds what the converter writes rather than the value the comparison was given.

### Multi-value fields

A field that holds more than one value is tested as a whole, with the function that answers the test over every value at once.

```csharp
.Where(p => p.Tags.Any(t => t == "water"))        // WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
.Where(p => p.Tags.Contains("water"))             // WHERE (tags IS NOT NULL AND MATCH(tags, "water"))
.Where(p => p.Tags.Any())                         // WHERE tags IS NOT NULL
.Where(p => p.Ratings.Any(r => r > 3))            // WHERE (ratings IS NOT NULL AND MV_MAX(ratings) > 3)
.Where(p => p.Ratings.All(r => r > 3))            // WHERE (ratings IS NULL OR MV_MIN(ratings) > 3)
.Where(p => p.Tags.Any(t => wanted.Contains(t)))  // WHERE (tags IS NOT NULL AND (MATCH(tags, "iot") OR MATCH(tags, "water")))
```

`All` over equality asks for one distinct value that matches, since every value being equal to the same one means there is only one: `MV_COUNT(MV_DEDUPE(tags)) == 1 AND MATCH(...)`. A missing field is an empty sequence, where `All` holds and `Any` does not, and each translation says so explicitly rather than leaving the predicate null. That covers `MATCH` too: a shard whose index does not map the field has it as null, and `MATCH` over null is null, which an enclosing `NOT` would keep null and so drop the document.

Any property typed as an `IEnumerable<T>` is accepted, a set and an interface included; a dictionary is one object in the mapping rather than a field of values, and is not. No collection instance exists when the query is translated, so a comparer on one is as invisible as a `StringComparison` on a scalar: the comparison is the one the store performs, not the one the collection would.

The value the elements are compared with is rendered as in any other predicate: a captured variable inline or as a parameter, `DateTime.UtcNow.AddDays(-7)` as `NOW() - 7 days`, and an enum as the serializer writes it, by name or by number. A comparison with another field is refused, and so is one with null, which Elasticsearch does not store among the values.

A predicate compared with `true` or `false` is written as the predicate or its negation, so `p.Tags.Any() == false` becomes `NOT tags IS NOT NULL`: Elasticsearch takes neither `MATCH` nor `IS NOT NULL` as an operand of a comparison. A comparison with a boolean known only when the query runs is refused.

On a text-mapped field `MATCH` is an analyzed search rather than equality, so `Any(t => t == "water bottle")` also matches a document whose tags are `["water"]`. Map the field as a keyword where the distinction matters. `MV_CONTAINS` and `MV_INTERSECTS` are the exact primitives for this, in preview since 9.2 and 9.4; they replace `MATCH` here once they are generally available.

Elasticsearch does not allow `MATCH` after `LIMIT`, `STATS` or `FORK`, so a predicate that translates to it is refused after `Take`, `GroupBy`, `Fork` or a `RawEsql` fragment holding one of those commands, in a `Fork` branch that follows them as well, when the query is translated, rather than failing when it runs. Put the `Where` before them; `MV_CONTAINS` and `MV_INTERSECTS` lift this as well once they are generally available.

A test that holds for one value at a time, such as `StartsWith`, is refused: it needs the field read position by position, which the functions above do not do. A LINQ operator over the field itself, such as `p.Tags.Where(t => t != "")`, is refused for the same reason, and so is a row that a `Select` made the collection itself, `Select(p => p.Tags)`, which has no field name to test: project the collection into a member instead. So is membership that every value must pass, `All(t => wanted.Contains(t))`, or that some value must fail, `Any(t => !wanted.Contains(t))`: `MATCH` answers whether some value is one of them, and `!p.Tags.Any(t => wanted.Contains(t))` asks that none is. An OR inside the predicate is written as two `Any` joined with `||`.

Five more shapes are refused: a `Contains` that takes an equality comparer, which the comparison Elasticsearch performs would not follow; membership in a captured set, dictionary or collection type of your own, which may compare its values in a way of its own, where an array, a `List`, a `ReadOnlyCollection`, a LINQ query, an iterator method or a collection expression compares with default equality; membership in more than 256 values, each of which adds a level to the expression Elasticsearch parses; a collection written through a `JsonConverter`, on the property, on its type or among the serializer's converters, whose field holds what the converter writes rather than the values compared; and a collection of objects, for which ES|QL has a column for each field of the objects and none for the objects themselves. A class the serializer writes as a value, such as `Uri`, is compared as a value.

### Captured variables and parameterization

Captured C# variables are inlined by default:

```csharp
var minStatus = 400;
query.Where(l => l.StatusCode >= minStatus)
// WHERE statusCode >= 400
```

Use `.ToEsqlString(inlineParameters: false)` to extract them as named `?param` placeholders:

```csharp
var esql = query
    .Where(l => l.StatusCode >= minStatus)
    .ToEsqlString(inlineParameters: false);
// WHERE statusCode >= ?minStatus
```

The parameter values are retrievable via `.GetParameters()` for passing to the ES|QL API.

## STATS...BY - aggregation

`.GroupBy()` followed by `.Select()` translates to the ES|QL [`STATS...BY`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-stats-by) command:

```csharp
query
    .GroupBy(l => l.Level)
    .Select(g => new
    {
        Level = g.Key,
        Count = g.Count(),
        AvgDuration = g.Average(l => l.Duration)
    })
```

```
FROM logs-*
| STATS count = COUNT(*), avgDuration = AVG(duration) BY level = log.level
```

### Composite group keys

```csharp
query
    .GroupBy(l => new { l.Level, l.Host })
    .Select(g => new
    {
        Level = g.Key.Level,
        Host = g.Key.Host,
        Count = g.Count()
    })
```

```
FROM logs-*
| STATS count = COUNT(*) BY level = log.level, host
```

### ES|QL grouping functions

Use `EsqlFunctions.Bucket()`, `EsqlFunctions.TBucket()`, and `EsqlFunctions.Categorize()` in group keys:

```csharp
query
    .GroupBy(l => EsqlFunctions.Bucket(l.Duration, 10))
    .Select(g => new { Bucket = g.Key, Count = g.Count() })
// STATS count = COUNT(*) BY bucket = BUCKET(duration, 10)

query
    .GroupBy(l => EsqlFunctions.TBucket(l.Timestamp, "1 hour"))
    .Select(g => new { Hour = g.Key, Count = g.Count() })
// STATS count = COUNT(*) BY hour = TBUCKET(@timestamp, "1 hour")
```

### Terminal aggregation operators

Aggregation methods called directly on the queryable produce `STATS` without `BY`:

```csharp
query.Where(l => l.Level == "ERROR").Count()
// FROM logs-* | WHERE log.level == "ERROR" | STATS count = COUNT(*)

query.Sum(l => l.Duration)
// FROM logs-* | STATS sum = SUM(duration)
```

### Advanced aggregation functions

Beyond standard LINQ aggregations (`Count`, `Sum`, `Average`, `Min`, `Max`), ES|QL-specific aggregations are available through `EsqlFunctions`:

```csharp
.Select(g => new
{
    P99 = EsqlFunctions.Percentile(g, l => l.Duration, 99),
    Med = EsqlFunctions.Median(g, l => l.Duration),
    Distinct = EsqlFunctions.CountDistinct(g, l => l.Host),
    Dev = EsqlFunctions.StdDev(g, l => l.Duration),
    Vals = EsqlFunctions.Values(g, l => l.Host)
})
```

See the [functions reference](functions-reference.md) for the complete list of aggregation functions.

## SORT - ordering

`.OrderBy()` and `.OrderByDescending()` translate to the ES|QL [`SORT`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-sort) command:

```csharp
query
    .OrderBy(l => l.Level)
    .ThenByDescending(l => l.Timestamp)
```

```
FROM logs-*
| SORT log.level, @timestamp DESC
```

## LIMIT - row count

`.Take()` translates to the ES|QL [`LIMIT`](elasticsearch://reference/query-languages/esql/esql-commands.md#esql-limit) command:

```csharp
query.Take(100)
// | LIMIT 100
```

`.First()` and `.FirstOrDefault()` produce `LIMIT 1`. `.Single()` and `.SingleOrDefault()` produce `LIMIT 2` (to validate exactly one result).

## KEEP - projection

`.Select()` translates to `KEEP` for simple field selections and `EVAL` for computed fields:

```csharp
// Simple projection → KEEP
query.Select(l => new { l.Message, l.Timestamp })
// | KEEP message, @timestamp

// Computed fields → EVAL + KEEP
query.Select(l => new { l.Message, Secs = l.Duration / 1000 })
// | EVAL secs = (duration / 1000)
// | KEEP message, secs

// Function calls → EVAL
query.Select(l => new { Upper = l.Message.ToUpper(), Hour = l.Timestamp.Hour })
// | EVAL upper = TO_UPPER(message), hour = DATE_EXTRACT("hour_of_day", @timestamp)
```

### Conditional projections

The ternary operator translates to `CASE`:

```csharp
query.Select(l => new { Status = l.StatusCode >= 500 ? "error" : "ok" })
// | EVAL status = CASE WHEN statusCode >= 500 THEN "error" ELSE "ok" END
```

### Renamed fields

When a property name in the anonymous type differs from the source field name, a `RENAME` command is generated:

```csharp
query.Select(l => new { Msg = l.Message })
// | RENAME message AS msg
// | KEEP msg
```

### Nested anonymous projections

Nested anonymous projections are flattened to dotted field names:

```csharp
query.Select(l => new { A = new { B = l.Message } })
// | RENAME message AS a.b
// | KEEP a.b
```

Consecutive `Select` calls can still be merged through nested member access:

```csharp
query
    .Select(l => new { A = new { B = l.Message } })
    .Select(x => x.A.B)
// | KEEP message
```

### Null-guarded nested projections

A selection of a nested object usually arrives guarded, the shape a GraphQL layer emits for `parent { child }`. The guard is dropped and the leaves are projected, which is exact because a branch whose leaf columns are all null is omitted from the row, so the member falls back to its default:

```csharp
query.Select(l => new LogDto
{
    Host = l.Host == null ? null : new HostDto { Name = l.Host.Name }
})
// | KEEP host.name
```

The guard has to read through the path it tests: `l.Host == null ? null : new HostDto { Name = l.Agent.Name }` is refused, since dropping it would give a value to a row that has no host.

Two consequences are worth stating. A parent that exists but whose projected leaves are all null comes back as null, the same as a missing parent, because the row carries nothing to tell the two apart. And the member has to be able to hold that null, so it cannot be declared non-nullable, while a member without an annotation, as a consumer building without nullable reference types has everywhere, passes; the "without an initializer" half of that condition cannot be checked at translation time, so `HostDto? Host { get; set; } = new()` yields an empty object rather than null.

## KEEP and DROP extensions

In addition to `.Select()`, explicit `.Keep()` and `.Drop()` extension methods are available for fine-grained control:

### KEEP with string field names

```csharp
query.Keep("message", "statusCode")
// | KEEP message, statusCode
```

### KEEP with lambda selectors

```csharp
query.Keep(l => l.Message, l => l.StatusCode)
// | KEEP message, statusCode

query.Keep(l => l.Host)
// | KEEP host.*
```

Lambda selectors resolve field names from `[JsonPropertyName]` attributes automatically.
Selecting a complex object member expands to a wildcard keep (`field.*`) so ES|QL returns flattened sub-fields.

### KEEP with projection

```csharp
query.Keep(l => new { l.Message, l.StatusCode })
// | KEEP message, statusCode

query.Keep(l => new { Msg = l.Message })
// | RENAME message AS msg
// | KEEP msg

query.Keep(l => new { l.Host })
// | KEEP host.*

// Object aliases are not supported (ES|QL has no equivalent for renaming field.*)
query.Keep(l => new { Node = l.Host }) // throws NotSupportedException
```

### DROP with string field names

```csharp
query.Drop("duration", "host")
// | DROP duration, host
```

### DROP with lambda selectors

```csharp
query.Drop(l => l.Duration, l => l.Host)
// | DROP duration, host

query.Drop(l => l.Host)
// | DROP host.*
```

## LOOKUP JOIN - cross-index correlation

ES|QL's `LOOKUP JOIN` command correlates data from a lookup index. Multiple API styles are supported.

### LookupJoin with key selectors

The most explicit form takes a string index name and key selectors:

```csharp
query
    .From("employees")
    .LookupJoin<LogEntry, LanguageLookup, int, object>(
        "languages_lookup",
        outer => outer.StatusCode,
        inner => inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

When outer and inner key selectors reference the same field name, a simple `ON` clause is generated:

```csharp
outer => outer.ClientIp,
inner => inner.ClientIp,
// ON clientIp (instead of ON clientIp == clientIp)
```

### LookupJoin with predicate

Use an expression-based `ON` condition for more complex join logic:

```csharp
query
    .From("employees")
    .LookupJoin<LogEntry, LanguageLookup, object>(
        "languages_lookup",
        (outer, inner) => outer.StatusCode == inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

### LeftJoin with IQueryable inner

Use `LeftJoin` when the inner source is another `EsqlQueryable` with `.From()`:

```csharp
var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

query
    .From("employees")
    .LeftJoin(
        lookup,
        outer => outer.StatusCode,
        inner => inner.LanguageCode,
        (outer, inner) => new { outer.Message, inner!.LanguageName }
    )
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

### Standard LINQ Join (inner join)

The standard `Queryable.Join` is also supported. It translates to `LOOKUP JOIN` followed by `WHERE key IS NOT NULL` to enforce inner join semantics:

```csharp
query
    .From("firewall_logs")
    .Join(
        lookup,
        outer => outer.ClientIp,
        inner => inner.ClientIp,
        (outer, inner) => new { outer.Message, inner.ThreatLevel }
    )
```

```
FROM firewall_logs
| LOOKUP JOIN threat_list ON clientIp
| WHERE clientIp IS NOT NULL
| KEEP message, threatLevel
```

### LINQ query syntax (left outer join)

The standard LINQ query syntax for left outer joins works naturally:

```csharp
var lookup = new EsqlQueryable<LanguageLookup>().From("languages_lookup");

var esql = (
    from outer in query.From("employees")
    join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
    from inner in ps.DefaultIfEmpty()
    select new { outer.Message, inner!.LanguageName }
).ToString();
```

```
FROM employees
| LOOKUP JOIN languages_lookup ON statusCode == languageCode
| KEEP message, languageName
```

### Complex projections in join result selectors

The result selector supports computed fields, renames, and null guards:

```csharp
// Computed field → EVAL
(outer, inner) => new { Msg = outer.Message, Lang = inner!.LanguageName.ToUpperInvariant() }
// | RENAME message AS msg
// | EVAL lang = TO_UPPER(languageName)
// | KEEP msg, lang

// Null guard → dropped, since the branch reads through the guarded path
(outer, inner) => new { LanguageName = inner == null ? null : inner.LanguageName }
// | KEEP languageName
```

A guard on the lookup side is dropped only when the branch it guards reads through the guarded path: a row with no match leaves the column null, which is the null the guard produces. A branch that reads elsewhere, as in `inner == null ? null : outer.Message`, keeps its own condition and is refused, since dropping it would give an unmatched row a value.

### Chaining multiple joins

```csharp
query
    .From("system_metrics")
    .LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
        "host_inventory", outer => outer.ClientIp, inner => inner.ClientIp, (o, i) => o)
    .LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
        "ownerships", outer => outer.ServerName, inner => inner.ClientIp, (o, i) => o)
```

```
FROM system_metrics
| LOOKUP JOIN host_inventory ON clientIp
| LOOKUP JOIN ownerships ON serverName == clientIp
```

## ROW - literal values

The `.Row()` extension method produces a `ROW` source command with literal values:

```csharp
query
    .Row(() => new { a = 1, b = "hello" })
    .ToString()
```

```
ROW a = 1, b = "hello"
```

This is primarily used with `COMPLETION` for standalone LLM prompts without querying an index.

## FORK and FUSE - hybrid search

`.Fork(...)` and `.Fuse(...)` translate to the ES|QL [`FORK`](elasticsearch://reference/query-languages/esql/commands/fork.md) and [`FUSE`](elasticsearch://reference/query-languages/esql/commands/fuse.md) commands respectively. Together they enable [hybrid search](vector-search.md#hybrid-search-fork--fuse): run multiple ranking pipelines in parallel and merge them with Reciprocal Rank Fusion (RRF) or linear combination.

```csharp
client.CreateQuery<Book>()
    .From("books", MetadataField.Id | MetadataField.Index | MetadataField.Score)
    .Fork(
        b => b.Where(x => EsqlFunctions.Match(x.Title, "vegetarian curry")).Take(50),
        b => b.Where(x => EsqlFunctions.Knn(x.TitleVec, queryVec)).Take(50))
    .Fuse(method: FuseMethod.Linear, normalizer: ScoreNormalizer.MinMax, weights: [0.7, 0.3])
    .OrderByDescending(_ => EsqlMetadata.Score)
    .Take(10);
```

```
FROM books METADATA _id, _index, _score
| FORK (WHERE MATCH(title, "vegetarian curry") | LIMIT 50) (WHERE KNN(titleVec, [...]) | LIMIT 50)
| FUSE LINEAR WITH { "normalizer": "minmax", "weights": { "fork1": 0.7, "fork2": 0.3 } }
| SORT _score DESC
| LIMIT 10
```

See the [vector and hybrid search guide](vector-search.md) for the complete API, including `weights` validation, custom score / group / key columns, and KNN options.

## COMPLETION - LLM inference

`.Completion()` translates to the ES|QL `COMPLETION` command. It sends a field to a configured inference endpoint and returns the result as a new column. See the [COMPLETION docs](completion.md) for full details.

```csharp
query
    .Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")
```

```
FROM logs-*
| COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
```

The lambda overload resolves field names from your type. A string overload is also available for raw field names:

```csharp
query.Completion("message", "my-custom-endpoint", column: "result")
```

## MultiField access

Access sub-fields of multi-field mappings using `.MultiField()`:

```csharp
.Where(l => l.Message.MultiField("keyword") == "exact match")
// WHERE message.keyword == "exact match"
```

## Named parameterization

Captured C# variables can be parameterized instead of inlined:

```csharp
var minStatus = 400;
var level = "ERROR";

var esql = query
    .Where(l => l.StatusCode >= minStatus && l.Level == level)
    .ToEsqlString(inlineParameters: false);
```

```
FROM logs-*
| WHERE (statusCode >= ?minStatus AND log.level == ?level)
```

Parameters are extracted separately via `.GetParameters()` for passing to the ES|QL API. When `inlineParameters` is `true` (the default), values are embedded directly in the query string.

## Unsupported operations

| LINQ method | Reason |
|---|---|
| `.Skip()` | ES\|QL does not support offset-based pagination |
| `.Distinct()` | Use `.GroupBy()` instead |
| Nested subqueries | ES\|QL does not support subqueries |
