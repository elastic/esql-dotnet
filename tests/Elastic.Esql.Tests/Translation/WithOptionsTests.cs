// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class WithOptionsTests : EsqlTestBase
{
	[Test]
	public void WithOptions_SetsQueryOptions()
	{
		var options = new TestQueryOptions(TimeZone: "UTC");

		var result = CreateQuery<LogEntry>()
			.WithOptions(options)
			.From("logs-*")
			.AsEsqlQueryable()
			.GetQueryOptions();

		_ = result.Should().BeOfType<TestQueryOptions>();
		_ = ((TestQueryOptions)result!).TimeZone.Should().Be("UTC");
	}

	[Test]
	public void WithOptions_DoesNotAffectEsqlOutput()
	{
		var withOptions = CreateQuery<LogEntry>()
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		var withoutOptions = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.ToString();

		_ = withOptions.Should().Be(withoutOptions);
	}

	[Test]
	public void WithOptions_LastCallWins()
	{
		var result = (TestQueryOptions)CreateQuery<LogEntry>()
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.WithOptions(new TestQueryOptions(TimeZone: "America/New_York", Locale: "en-US"))
			.From("logs-*")
			.AsEsqlQueryable()
			.GetQueryOptions()!;

		_ = result.TimeZone.Should().Be("America/New_York");
		_ = result.Locale.Should().Be("en-US");
	}

	[Test]
	public void WithOptions_SurvivesLinqChain()
	{
		var result = CreateQuery<LogEntry>()
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.OrderByDescending(l => l.Timestamp)
			.Take(50)
			.AsEsqlQueryable()
			.GetQueryOptions();

		_ = result.Should().BeOfType<TestQueryOptions>();
		_ = ((TestQueryOptions)result!).TimeZone.Should().Be("UTC");
	}

	[Test]
	public void WithoutOptions_GetQueryOptionsReturnsNull()
	{
		var result = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable()
			.GetQueryOptions();

		_ = result.Should().BeNull();
	}
}
