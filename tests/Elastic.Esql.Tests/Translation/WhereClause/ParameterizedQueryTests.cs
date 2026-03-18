// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class ParameterizedQueryTests : EsqlTestBase
{
	[Test]
	public void ToEsqlString_DefaultInlines_CapturedInt()
	{
		var threshold = 500;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.ToEsqlString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedInt()
	{
		var threshold = 500;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= ?threshold
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedString()
	{
		var level = "ERROR";

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level.MultiField("keyword") == level)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE log.level.keyword == ?level
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedDateTime()
	{
		var cutoff = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > cutoff)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE @timestamp > ?cutoff
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedDouble()
	{
		var maxDuration = 1000.5;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Duration > maxDuration)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE duration > ?maxDuration
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedBool()
	{
		var errorOnly = true;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.IsError == errorOnly)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE isError == ?errorOnly
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CapturedEnum()
	{
		var level = LogLevel.Error;

		var esql = CreateQuery<EventDocument>()
			.From("events-*")
			.Where(e => e.Level == level)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM events-*
			| WHERE level == ?level
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_MultipleVariables()
	{
		var minStatus = 400;
		var level = "ERROR";

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= minStatus && l.Level.MultiField("keyword") == level)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (statusCode >= ?minStatus AND log.level.keyword == ?level)
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_DuplicateNames_GetSuffix()
	{
		var value = 100;
		var value2 = 200;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= value && l.StatusCode <= value2)
			.ToEsqlString(inlineParameters: false);

		// Both captured vars are named "value" variants but from different closures
		// "value" first, then "value2" is a different variable name so no dedup needed
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (statusCode >= ?value AND statusCode <= ?value2)
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_SameNamedVariable_Deduplicates()
	{
		// Both WHERE clauses use the same variable pattern
		var threshold = 100;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.Where(l => l.StatusCode <= threshold)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= ?threshold
			| WHERE statusCode <= ?threshold_2
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_NestedMemberAccess_UsesLeafName()
	{
		var config = new { MaxRetries = 3 };

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode == config.MaxRetries)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode == ?MaxRetries
			""".NativeLineEndings());
	}

	[Test]
	public void GetParameters_ReturnsParameterDictionary()
	{
		var threshold = 500;
		var level = "ERROR";

		var queryable = (IEsqlQueryable<LogEntry>)CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold && l.Level.MultiField("keyword") == level);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters!.Parameters.Should().HaveCount(2);

		var thresholdParam = parameters.Parameters["threshold"];
		_ = thresholdParam.ValueKind.Should().Be(JsonValueKind.Number);
		_ = thresholdParam.GetInt32().Should().Be(500);

		var levelParam = parameters.Parameters["level"];
		_ = levelParam.ValueKind.Should().Be(JsonValueKind.String);
		_ = levelParam.GetString().Should().Be("ERROR");
	}

	[Test]
	public void GetParameters_NoVariables_ReturnsNull()
	{
		var queryable = (IEsqlQueryable<LogEntry>)CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().BeNull();
	}

	[Test]
	public void GetParameters_Enum_StoresJsonElement()
	{
		var level = LogLevel.Error;

		var queryable = (IEsqlQueryable<EventDocument>)CreateQuery<EventDocument>()
			.From("events-*")
			.Where(e => e.Level == level);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();

		var levelParam = parameters!.Parameters["level"];
		_ = levelParam.ValueKind.Should().Be(JsonValueKind.String);
		_ = levelParam.GetString().Should().Be("Error");
	}

	[Test]
	public void ToEsqlString_Parameterized_LikePatternStaysInlined()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.MultiField("keyword").Contains("error"))
			.ToEsqlString(inlineParameters: false);

		// LIKE patterns stay inlined, they are not captured variables
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message.keyword LIKE "*error*"
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_StaticMemberStaysInlined()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Timestamp > DateTime.UtcNow)
			.ToEsqlString(inlineParameters: false);

		// Static members like DateTime.UtcNow translate to NOW(), not parameterized
		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE @timestamp > NOW()
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlParams_ProducesCorrectFormat()
	{
		var threshold = 500;
		var level = "ERROR";

		var queryable = (IEsqlQueryable<LogEntry>)CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold && l.Level.MultiField("keyword") == level);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters!.Parameters.Should().HaveCount(2);

		_ = parameters.Parameters["threshold"].GetInt32().Should().Be(500);
		_ = parameters.Parameters["level"].GetString().Should().Be("ERROR");
	}

	[Test]
	public void ToString_DefaultsToInlined()
	{
		var threshold = 500;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= threshold)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Inline_OrdinalEnum_EmitsInteger()
	{
		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => d.Priority == Priority.High)
			.ToEsqlString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE priority == 2
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_OrdinalEnum()
	{
		var prio = Priority.High;

		var esql = CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => d.Priority == prio)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE priority == ?prio
			""".NativeLineEndings());
	}

	[Test]
	public void GetParameters_OrdinalEnum_StoresIntegerValue()
	{
		var prio = Priority.High;

		var queryable = (IEsqlQueryable<OrdinalEnumDocument>)CreateQuery<OrdinalEnumDocument>()
			.From("docs-*")
			.Where(d => d.Priority == prio);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();

		var prioParam = parameters!.Parameters["prio"];
		_ = prioParam.ValueKind.Should().Be(JsonValueKind.Number);
		_ = prioParam.GetInt32().Should().Be(2);
	}

	[Test]
	public void ToEsqlString_Inline_CustomConverter_UsesSerializedForm()
	{
		var esql = CreateQuery<CustomConverterDocument>()
			.From("docs-*")
			.Where(d => d.CustomId == 42)
			.ToEsqlString();

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE customId == "ID-42"
			""".NativeLineEndings());
	}

	[Test]
	public void ToEsqlString_Parameterized_CustomConverter()
	{
		var someId = 42;

		var esql = CreateQuery<CustomConverterDocument>()
			.From("docs-*")
			.Where(d => d.CustomId == someId)
			.ToEsqlString(inlineParameters: false);

		_ = esql.Should().Be(
			"""
			FROM docs-*
			| WHERE customId == ?someId
			""".NativeLineEndings());
	}

	[Test]
	public void GetParameters_CustomConverter_StoresSerializedValue()
	{
		var someId = 42;

		var queryable = (IEsqlQueryable<CustomConverterDocument>)CreateQuery<CustomConverterDocument>()
			.From("docs-*")
			.Where(d => d.CustomId == someId);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();

		var idParam = parameters!.Parameters["someId"];
		_ = idParam.ValueKind.Should().Be(JsonValueKind.String);
		_ = idParam.GetString().Should().Be("ID-42");
	}
}
