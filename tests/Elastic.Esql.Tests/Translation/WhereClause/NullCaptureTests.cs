// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NullCaptureTests : EsqlTestBase
{
	[Test]
	public void Where_CapturedNullString_EmitsIsNull()
	{
		string? clientIp = null;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.ClientIp == clientIp)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE clientIp IS NULL
			""".NativeLineEndings());
	}

	[Test]
	public void Where_CapturedNullNullableInt_NotEqual_EmitsIsNotNull()
	{
		int? statusCode = null;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode != statusCode)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode IS NOT NULL
			""".NativeLineEndings());
	}

	[Test]
	public void Where_CapturedNullOnLeft_EmitsIsNull()
	{
		string? clientIp = null;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => clientIp == l.ClientIp)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE clientIp IS NULL
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ComparisonEqualsNullCapture_ParenthesizesComparison()
	{
		bool? flag = null;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => (l.Duration > 1) == flag)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (duration > 1.0) IS NULL
			""".NativeLineEndings());
	}
}
