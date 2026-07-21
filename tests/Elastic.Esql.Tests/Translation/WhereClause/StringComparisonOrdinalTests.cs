// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class StringComparisonOrdinalTests : EsqlTestBase
{
	[Test]
	public void Where_ContainsWithOrdinal_TranslatesToLike()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains("err", StringComparison.Ordinal))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message LIKE "*err*"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_IndexOfWithOrdinal_TranslatesToLocate()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.IndexOf("x", StringComparison.Ordinal) >= 0)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE (LOCATE(message, "x") - 1) >= 0
			""".NativeLineEndings());
	}

	[Test]
	public void Where_ContainsWithOrdinalIgnoreCase_ThrowsNotSupported()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains("err", StringComparison.OrdinalIgnoreCase));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*other than StringComparison.Ordinal*");
	}

	[Test]
	public void Select_IndexOfWithOrdinalIgnoreCase_ThrowsNotSupported()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Position = l.Message.IndexOf("x", StringComparison.OrdinalIgnoreCase) });

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*other than StringComparison.Ordinal*");
	}

	[Test]
	public void Where_StartsWithOrdinal_TranslatesToLike()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.StartsWith("err", StringComparison.Ordinal))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message LIKE "err*"
			""".NativeLineEndings());
	}

	[Test]
	public void Where_EndsWithOrdinal_TranslatesToLike()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.EndsWith("err", StringComparison.Ordinal))
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE message LIKE "*err"
			""".NativeLineEndings());
	}
}
