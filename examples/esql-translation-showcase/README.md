# ES|QL Translation Showcase

Short, translation-only demo app for a 5-minute technical presentation.

## Run

```bash
dotnet run --project examples/esql-translation-showcase
```

No Elasticsearch cluster is required. The app only prints generated ES|QL.

## 5-minute speaker flow

- **0:00 - 0:40**: LINQ quick intro and why `IQueryable` matters.
- **0:40 - 1:20**: Scenario 1 basic method syntax (`Where`, `OrderBy`, `Take`).
- **1:20 - 1:50**: Scenario 2 query syntax (`from/where/select`) to show idiomatic parity.
- **1:50 - 2:40**: Scenario 3 expression tree peek.
- **2:40 - 3:40**: Scenario 4 closure-captured variables (inline vs `?param`).
- **3:40 - 4:30**: Scenario 5 projection "magic" (Select merging and nested flattening).
- **4:30 - 5:00**: Scenario 6 `GroupBy`/`STATS` and `LOOKUP JOIN`, then recap.

## Files

- `Program.cs`: all scenarios in presentation order.
