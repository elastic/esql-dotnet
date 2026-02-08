// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Tests.MappingParity;

/// <summary>
/// Verifies that context-based (source-generated) and attribute-based (reflection)
/// resolution paths produce identical ES|QL queries and materialization results.
/// </summary>
public class ContextVsReflectionTests
{
	private static readonly EsqlClient WithContext = EsqlClient.InMemory(EsqlTestMappingContext.Instance);
	private static readonly EsqlClient WithoutContext = EsqlClient.InMemory();

	private static readonly TypeFieldMetadataResolver ContextResolver = new(EsqlTestMappingContext.Instance);
	private static readonly TypeFieldMetadataResolver ReflectionResolver = new();

	// ================================================================
	// A. Resolution Path Verification
	// ================================================================

	[Test]
	public void Context_ResolvesViaContext()
	{
		var source = ContextResolver.GetResolutionSource(typeof(LogEntry));
		_ = source.Should().Be(MetadataSource.Context);
	}

	[Test]
	public void Reflection_ResolvesViaAttributes()
	{
		var source = ReflectionResolver.GetResolutionSource(typeof(LogEntry));
		_ = source.Should().Be(MetadataSource.Attributes);
	}

	[Test]
	public void SimpleDocument_Context_ResolvesViaContext()
	{
		var source = ContextResolver.GetResolutionSource(typeof(SimpleDocument));
		_ = source.Should().Be(MetadataSource.Context);
	}

	[Test]
	public void SimpleDocument_Reflection_ResolvesViaAttributes()
	{
		var source = ReflectionResolver.GetResolutionSource(typeof(SimpleDocument));
		_ = source.Should().Be(MetadataSource.Attributes);
	}

	// ================================================================
	// B. Index Pattern Parity
	// ================================================================

	[Test]
	public void LogEntry_FromClause_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>().ToString();
		var withoutCtx = WithoutContext.Query<LogEntry>().ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void MetricDocument_FromClause_Matches()
	{
		var withCtx = WithContext.Query<MetricDocument>().ToString();
		var withoutCtx = WithoutContext.Query<MetricDocument>().ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void EventDocument_FromClause_Matches()
	{
		var withCtx = WithContext.Query<EventDocument>().ToString();
		var withoutCtx = WithoutContext.Query<EventDocument>().ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void SimpleDocument_FromClause_Matches()
	{
		var withCtx = WithContext.Query<SimpleDocument>().ToString();
		var withoutCtx = WithoutContext.Query<SimpleDocument>().ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// C. Field Name Resolution Parity
	// ================================================================

	[Test]
	public void JsonPropertyName_ResolvesIdentically()
	{
		// LogEntry.Level has [JsonPropertyName("log.level")]
		var ctxField = ContextResolver.Resolve(typeof(LogEntry).GetProperty("Level")!);
		var reflField = ReflectionResolver.Resolve(typeof(LogEntry).GetProperty("Level")!);

		_ = ctxField.Should().Be(reflField);
		_ = ctxField.Should().Be("log.level");
	}

	[Test]
	public void Timestamp_JsonPropertyName_ResolvesIdentically()
	{
		// LogEntry.Timestamp has [JsonPropertyName("@timestamp")]
		var ctxField = ContextResolver.Resolve(typeof(LogEntry).GetProperty("Timestamp")!);
		var reflField = ReflectionResolver.Resolve(typeof(LogEntry).GetProperty("Timestamp")!);

		_ = ctxField.Should().Be(reflField);
		_ = ctxField.Should().Be("@timestamp");
	}

	[Test]
	public void CamelCase_ResolvesIdentically()
	{
		// LogEntry.StatusCode → "statusCode"
		var ctxField = ContextResolver.Resolve(typeof(LogEntry).GetProperty("StatusCode")!);
		var reflField = ReflectionResolver.Resolve(typeof(LogEntry).GetProperty("StatusCode")!);

		_ = ctxField.Should().Be(reflField);
		_ = ctxField.Should().Be("statusCode");
	}

	[Test]
	public void SimpleProperty_ResolvesIdentically()
	{
		// LogEntry.Message → "message"
		var ctxField = ContextResolver.Resolve(typeof(LogEntry).GetProperty("Message")!);
		var reflField = ReflectionResolver.Resolve(typeof(LogEntry).GetProperty("Message")!);

		_ = ctxField.Should().Be(reflField);
		_ = ctxField.Should().Be("message");
	}

	// ================================================================
	// D. Enum Handling Parity
	// ================================================================

	[Test]
	public void EnumComparison_ProducesIdenticalEsql()
	{
		var withCtx = WithContext.Query<EventDocument>()
			.Where(e => e.Level == LogLevel.Error)
			.ToString();

		var withoutCtx = WithoutContext.Query<EventDocument>()
			.Where(e => e.Level == LogLevel.Error)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// E. WHERE Clause Parity
	// ================================================================

	[Test]
	public void Where_Equality_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_Comparison_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_StringContains_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.Message.Contains("timeout"))
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.Message.Contains("timeout"))
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_NullCheck_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.ClientIp != null)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.ClientIp != null)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_BooleanField_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.IsError)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.IsError)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_CapturedVariable_Matches()
	{
		var threshold = 500;
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Where_LogicalOperators_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Where(l => l.Level == "ERROR" || l.Level == "WARNING")
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Where(l => l.Level == "ERROR" || l.Level == "WARNING")
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// F. SELECT Projection Parity
	// ================================================================

	[Test]
	public void Select_FieldSubset_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Select(l => new { l.Message, l.Duration })
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Select(l => new { l.Message, l.Duration })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Select_RenamedField_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Select(l => new { l.Message, l.Timestamp })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void Select_ComputedField_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.Select(l => new { l.Message, DurationMs = l.Duration * 1000 })
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.Select(l => new { l.Message, DurationMs = l.Duration * 1000 })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// G. GROUP BY / STATS Parity
	// ================================================================

	[Test]
	public void GroupBy_SingleField_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void GroupBy_WithSum_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, TotalDuration = g.Sum(l => l.Duration) })
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// H. ORDER BY Parity
	// ================================================================

	[Test]
	public void OrderBy_Ascending_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.OrderBy(l => l.Timestamp)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.OrderBy(l => l.Timestamp)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void OrderBy_Descending_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.OrderByDescending(l => l.Duration)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.OrderByDescending(l => l.Duration)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	[Test]
	public void OrderBy_AttributedField_Matches()
	{
		var withCtx = WithContext.Query<LogEntry>()
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		var withoutCtx = WithoutContext.Query<LogEntry>()
			.OrderByDescending(l => l.Timestamp)
			.ToString();

		_ = withCtx.Should().Be(withoutCtx);
	}

	// ================================================================
	// I. JsonIgnore Parity
	// ================================================================

	[Test]
	public void JsonIgnore_BothPathsSkipIgnoredProperties()
	{
		var ctxIgnored = ContextResolver.IsIgnored(typeof(LogEntry).GetProperty("InternalId")!);
		var reflIgnored = ReflectionResolver.IsIgnored(typeof(LogEntry).GetProperty("InternalId")!);

		_ = ctxIgnored.Should().BeTrue();
		_ = reflIgnored.Should().BeTrue();
	}

	[Test]
	public void NonIgnored_BothPathsIncludeProperty()
	{
		var ctxIgnored = ContextResolver.IsIgnored(typeof(LogEntry).GetProperty("Message")!);
		var reflIgnored = ReflectionResolver.IsIgnored(typeof(LogEntry).GetProperty("Message")!);

		_ = ctxIgnored.Should().BeFalse();
		_ = reflIgnored.Should().BeFalse();
	}

	// ================================================================
	// J. Materialization Parity
	// ================================================================

	[Test]
	public void Materialize_BasicTypes_ProducesIdenticalObjects()
	{
		var response = CreateFakeResponse(
			["message", "statusCode", "duration", "isError"],
			["keyword", "integer", "double", "boolean"],
			[["test message", 200, 1.5, false]]
		);

		var ctxMaterializer = new ResultMaterializer(ContextResolver);
		var reflMaterializer = new ResultMaterializer(ReflectionResolver);

		var query = new EsqlQuery { ElementType = typeof(LogEntry) };

		var ctxResult = ctxMaterializer.Materialize<LogEntry>(response, query).ToList();
		var reflResult = reflMaterializer.Materialize<LogEntry>(response, query).ToList();

		_ = ctxResult.Should().HaveCount(1);
		_ = reflResult.Should().HaveCount(1);

		_ = ctxResult[0].Message.Should().Be(reflResult[0].Message);
		_ = ctxResult[0].StatusCode.Should().Be(reflResult[0].StatusCode);
		_ = ctxResult[0].Duration.Should().Be(reflResult[0].Duration);
		_ = ctxResult[0].IsError.Should().Be(reflResult[0].IsError);
	}

	[Test]
	public void Materialize_Enum_ProducesIdenticalObjects()
	{
		var response = CreateFakeResponse(
			["level", "message"],
			["keyword", "keyword"],
			[["Error", "test"]]
		);

		var ctxMaterializer = new ResultMaterializer(ContextResolver);
		var reflMaterializer = new ResultMaterializer(ReflectionResolver);

		var query = new EsqlQuery { ElementType = typeof(EventDocument) };

		var ctxResult = ctxMaterializer.Materialize<EventDocument>(response, query).ToList();
		var reflResult = reflMaterializer.Materialize<EventDocument>(response, query).ToList();

		_ = ctxResult[0].Level.Should().Be(reflResult[0].Level);
		_ = ctxResult[0].Level.Should().Be(LogLevel.Error);
	}

	[Test]
	public void Materialize_Nullable_ProducesIdenticalObjects()
	{
		var response = CreateFakeResponse(
			["name", "value", "count"],
			["keyword", "double", "integer"],
			[["cpu", 95.5, null]]
		);

		var ctxMaterializer = new ResultMaterializer(ContextResolver);
		var reflMaterializer = new ResultMaterializer(ReflectionResolver);

		var query = new EsqlQuery { ElementType = typeof(MetricDocument) };

		var ctxResult = ctxMaterializer.Materialize<MetricDocument>(response, query).ToList();
		var reflResult = reflMaterializer.Materialize<MetricDocument>(response, query).ToList();

		_ = ctxResult[0].Name.Should().Be(reflResult[0].Name);
		_ = ctxResult[0].Value.Should().Be(reflResult[0].Value);
		_ = ctxResult[0].Count.Should().Be(reflResult[0].Count);
		_ = ctxResult[0].Count.Should().BeNull();
	}

	[Test]
	public void Materialize_Guid_ProducesIdenticalObjects()
	{
		var guidStr = "d3b07384-d9a0-4e9a-8e1a-3b1c4c5d6e7f";
		var response = CreateFakeResponse(
			["eventId", "message", "level"],
			["keyword", "keyword", "keyword"],
			[[guidStr, "test", "Info"]]
		);

		var ctxMaterializer = new ResultMaterializer(ContextResolver);
		var reflMaterializer = new ResultMaterializer(ReflectionResolver);

		var query = new EsqlQuery { ElementType = typeof(EventDocument) };

		var ctxResult = ctxMaterializer.Materialize<EventDocument>(response, query).ToList();
		var reflResult = reflMaterializer.Materialize<EventDocument>(response, query).ToList();

		_ = ctxResult[0].EventId.Should().Be(reflResult[0].EventId);
		_ = ctxResult[0].EventId.Should().Be(Guid.Parse(guidStr));
	}

	// ================================================================
	// Helpers
	// ================================================================

	private static EsqlResponse CreateFakeResponse(
		string[] columnNames,
		string[] columnTypes,
		object?[][] rows)
	{
		var columns = columnNames.Select((name, i) => new EsqlColumn { Name = name, Type = columnTypes[i] }).ToList();
		var values = rows.Select(row => row.ToList()).ToList();

		return new EsqlResponse { Columns = columns, Values = values };
	}
}
