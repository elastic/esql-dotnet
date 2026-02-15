---
navigation_title: COMPLETION
---

# ES|QL COMPLETION command

The ES|QL `COMPLETION` command enables LLM inference directly within ES|QL queries. It sends a prompt to a configured inference endpoint and returns the completion result as a new column. This supports both RAG (retrieval-augmented generation) pipelines and standalone completions.

## Syntax

```
COMPLETION [column =] prompt WITH { "inference_id" : "endpoint-id" }
```

- **prompt** - a field reference or expression containing the prompt text
- **inference_id** - the inference endpoint ID configured in your Elasticsearch cluster
- **column** (optional) - output column name; defaults to `completion` if omitted

## Well-known inference endpoints

The `InferenceEndpoints` class provides constants for preconfigured serverless inference endpoints:

```csharp
using Elastic.Esql;

InferenceEndpoints.Anthropic.Claude46Opus    // ".anthropic-claude-4.6-opus-completion"
InferenceEndpoints.Anthropic.Claude45Sonnet  // ".anthropic-claude-4.5-sonnet-completion"
InferenceEndpoints.Anthropic.Claude45Opus    // ".anthropic-claude-4.5-opus-completion"
InferenceEndpoints.Anthropic.Claude37Sonnet  // ".anthropic-claude-3.7-sonnet-completion"
InferenceEndpoints.Google.Gemini25Pro        // ".google-gemini-2.5-pro-completion"
InferenceEndpoints.Google.Gemini25Flash      // ".google-gemini-2.5-flash-completion"
InferenceEndpoints.OpenAi.Gpt52             // ".openai-gpt-5.2-completion"
InferenceEndpoints.OpenAi.Gpt41             // ".openai-gpt-4.1-completion"
InferenceEndpoints.OpenAi.Gpt41Mini         // ".openai-gpt-4.1-mini-completion"
InferenceEndpoints.OpenAi.GptOss120B        // ".openai-gpt-oss-120b-completion"
InferenceEndpoints.Elastic.GpLlmV2          // ".gp-llm-v2-completion"
```

You can also pass any string as the inference ID for custom endpoints.

## Pipeline COMPLETION

Append `.Completion()` to a `FROM`-based query to run LLM inference on query results. This is the typical RAG pattern: retrieve documents, then send a field to the LLM.

### String-based prompt field

```csharp
var esql = client.Query<LogEntry>()
    .Where(l => l.StatusCode >= 500)
    .Take(1)
    .Completion("message", InferenceEndpoints.OpenAi.Gpt41, column: "analysis")
    .Keep("message", "analysis")
    .ToString();
```

```
FROM logs-*
| WHERE statusCode >= 500
| LIMIT 1
| COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
| KEEP message, analysis
```

### Lambda-based prompt field

Use a lambda selector for type-safe field resolution. Field names are resolved from your mapping context or `[JsonPropertyName]` attributes:

```csharp
var esql = client.Query<LogEntry>()
    .Completion(l => l.Message, InferenceEndpoints.Anthropic.Claude46Opus, column: "summary")
    .ToString();
```

```
FROM logs-*
| COMPLETION summary = message WITH { "inference_id" : ".anthropic-claude-4.6-opus-completion" }
```

### Without a column name

When `column` is omitted, ES|QL defaults the output column to `completion`:

```csharp
var esql = client.Query<LogEntry>()
    .Completion(l => l.Message, InferenceEndpoints.Google.Gemini25Flash)
    .ToString();
```

```
FROM logs-*
| COMPLETION message WITH { "inference_id" : ".google-gemini-2.5-flash-completion" }
```

## Standalone COMPLETION

`CompletionQuery.Generate()` creates a standalone `ROW + COMPLETION` pipeline for sending a prompt without querying an index:

```csharp
var esql = CompletionQuery.Generate(
    "Tell me about Elasticsearch",
    InferenceEndpoints.Anthropic.Claude46Opus,
    column: "answer"
);
```

```
ROW prompt = "Tell me about Elasticsearch"
| COMPLETION answer = prompt WITH { "inference_id" : ".anthropic-claude-4.6-opus-completion" }
```

The prompt text is automatically escaped for ES|QL string literals.

### Client convenience method

`EsqlClient` provides `CompletionAsync<T>()` for executing standalone completions directly:

```csharp
var results = await client.CompletionAsync<CompletionResult>(
    "Summarize the benefits of Elasticsearch",
    InferenceEndpoints.OpenAi.Gpt41,
    column: "answer"
);
```

## RAG pipeline example

A full retrieval-augmented generation pipeline that fetches error logs, builds a prompt, sends it to an LLM, and keeps only the relevant output:

```csharp
var esql = client.Query<LogEntry>()
    .Where(l => l.StatusCode >= 500)
    .OrderByDescending(l => l.Timestamp)
    .Take(10)
    .Completion(l => l.Message, InferenceEndpoints.OpenAi.Gpt41, column: "analysis")
    .Keep("message", "analysis")
    .ToString();
```

```
FROM logs-*
| WHERE statusCode >= 500
| SORT @timestamp DESC
| LIMIT 10
| COMPLETION analysis = message WITH { "inference_id" : ".openai-gpt-4.1-completion" }
| KEEP message, analysis
```

## ROW command

The `ROW` source command produces a row with literal values. It is primarily used with `COMPLETION` for standalone prompts, but is available as a general-purpose command:

```csharp
var query = new EsqlQuery();
query.AddCommand(new RowCommand("a = 1", "b = \"hello\""));
```

```
ROW a = 1, b = "hello"
```

## API reference

### Extension methods

| Method | Description |
|---|---|
| `.Completion(string prompt, string inferenceId, string? column)` | Adds a COMPLETION command with a string field name |
| `.Completion(Expression<Func<T, string>> prompt, string inferenceId, string? column)` | Adds a COMPLETION command with a type-safe lambda selector |

### Static factories

| Method | Description |
|---|---|
| `CompletionQuery.Generate(string prompt, string inferenceId, string? column)` | Generates a standalone `ROW + COMPLETION` ES\|QL string |

### Client methods

| Method | Description |
|---|---|
| `EsqlClient.CompletionAsync<T>(string prompt, string inferenceId, string? column, CancellationToken)` | Executes a standalone completion query against the cluster |
