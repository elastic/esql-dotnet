// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Tests.MappingParity;

/// <summary>
/// Verifies that context-based (source-generated) and reflection-based JSON metadata
/// resolution produce identical ES|QL queries. This is the core AOT guarantee: users
/// providing a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> must get
/// exactly the same queries as users relying on runtime reflection.
/// </summary>
public class ContextVsReflectionTests
{
	private static readonly EsqlQueryProvider ContextProvider = new(
		new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

	private static readonly EsqlQueryProvider ReflectionProvider = new(
		new JsonSerializerOptions
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

	private static EsqlQueryable<T> WithContext<T>() => new(ContextProvider);
	private static EsqlQueryable<T> WithReflection<T>() => new(ReflectionProvider);

	[Test]
	public void LogEntry_FromClause_Matches()
	{
		var withCtx = WithContext<LogEntry>().From("logs-*").ToString();
		var withoutCtx = WithReflection<LogEntry>().From("logs-*").ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void MetricDocument_FromClause_Matches()
	{
		var withCtx = WithContext<MetricDocument>().From("metrics-*").ToString();
		var withoutCtx = WithReflection<MetricDocument>().From("metrics-*").ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void EventDocument_FromClause_Matches()
	{
		var withCtx = WithContext<EventDocument>().From("events-*").ToString();
		var withoutCtx = WithReflection<EventDocument>().From("events-*").ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void SimpleDocument_FromClause_Matches()
	{
		var withCtx = WithContext<SimpleDocument>().From("simple-*").ToString();
		var withoutCtx = WithReflection<SimpleDocument>().From("simple-*").ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void JsonPropertyName_ResolvesIdentically()
	{
		// LogEntry.Level has [JsonPropertyName("log.level")]
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
		_ = withCtx.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level == "ERROR"
			""".NativeLineEndings());
	}

	[Test]
	public void Timestamp_JsonPropertyName_ResolvesIdentically()
	{
		// LogEntry.Timestamp has [JsonPropertyName("@timestamp")]
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
		_ = withCtx.Should().Be(
			"""
			FROM logs-*
			| SORT @timestamp DESC
			""".NativeLineEndings());
	}

	[Test]
	public void CamelCase_ResolvesIdentically()
	{
		// LogEntry.StatusCode resolves to "statusCode"
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
		_ = withCtx.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void SimpleProperty_ResolvesIdentically()
	{
		// LogEntry.Message resolves to "message"
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message == "hello")
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message == "hello")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
		_ = withCtx.Should().Be(
			"""
			FROM logs-*
			| WHERE message == "hello"
			""".NativeLineEndings());
	}

	[Test]
	public void EnumComparison_ProducesIdenticalEsql()
	{
		var withCtx = WithContext<EventDocument>()
			.From("events-*")
			.Where(e => e.Level == LogLevel.Error)
			.ToString();

		var withoutCtx = WithReflection<EventDocument>()
			.From("events-*")
			.Where(e => e.Level == LogLevel.Error)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_Equality_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_Comparison_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_StringContains_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains("timeout"))
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains("timeout"))
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_NullCheck_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.ClientIp != null)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.ClientIp != null)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_BooleanField_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_CapturedVariable_Matches()
	{
		var threshold = 500;

		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_LogicalOperators_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR" || l.Level == "WARNING")
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR" || l.Level == "WARNING")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Select_FieldSubset_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.Duration })
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.Duration })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Select_RenamedField_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Select_ComputedField_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, DurationMs = l.Duration * 1000 })
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, DurationMs = l.Duration * 1000 })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void GroupBy_SingleField_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void GroupBy_WithSum_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void OrderBy_Ascending_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Timestamp)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.OrderBy(l => l.Timestamp)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void OrderBy_Descending_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Duration)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Duration)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void OrderBy_AttributedField_Matches()
	{
		var withCtx = WithContext<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		var withoutCtx = WithReflection<LogEntry>()
			.From("logs-*")
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}
}
