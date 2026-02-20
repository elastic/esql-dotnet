// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
		_ = parameters.Parameters["threshold"].Should().Be(500);
		_ = parameters.Parameters["level"].Should().Be("ERROR");
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
	public void GetParameters_Enum_StoresStringValue()
	{
		var level = LogLevel.Error;

		var queryable = (IEsqlQueryable<EventDocument>)CreateQuery<EventDocument>()
			.From("events-*")
			.Where(e => e.Level == level);

		var parameters = queryable.GetParameters();

		_ = parameters.Should().NotBeNull();
		_ = parameters!.Parameters["level"].Should().Be("Error");
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
		var esqlParams = parameters!.ToEsqlParams();

		_ = esqlParams.Should().HaveCount(2);
		_ = esqlParams[0].Should().BeEquivalentTo(new Dictionary<string, object?> { ["threshold"] = 500 });
		_ = esqlParams[1].Should().BeEquivalentTo(new Dictionary<string, object?> { ["level"] = "ERROR" });
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
}
