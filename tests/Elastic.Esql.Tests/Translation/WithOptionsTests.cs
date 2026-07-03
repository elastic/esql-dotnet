// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class WithOptionsTests : EsqlTestBase
{
	[Test]
	public void WithOptions_ExecutorOptions_SetsExecutorOptions()
	{
		var options = new TestQueryOptions(TimeZone: "UTC");

		var result = CreateQuery<LogEntry>()
			.WithOptions(options)
			.From("logs-*")
			.AsEsqlQueryable()
			.GetExecutorOptions();

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
	public void WithOptions_SurvivesLinqChain()
	{
		var result = CreateQuery<LogEntry>()
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.OrderByDescending(l => l.Timestamp)
			.Take(50)
			.AsEsqlQueryable()
			.GetExecutorOptions();

		_ = result.Should().BeOfType<TestQueryOptions>();
		_ = ((TestQueryOptions)result!).TimeZone.Should().Be("UTC");
	}

	[Test]
	public void WithoutOptions_BothOptionSlotsReturnNull()
	{
		var query = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Level == "ERROR")
			.AsEsqlQueryable();

		_ = query.GetQueryOptions().Should().BeNull();
		_ = query.GetExecutorOptions().Should().BeNull();
	}

	[Test]
	public void WithOptions_CoreOptions_SetsQueryOptions()
	{
		var result = CreateQuery<LogEntry>()
			.WithOptions(new EsqlQueryOptions { TimeZone = "UTC", Locale = "en-US" })
			.From("logs-*")
			.AsEsqlQueryable()
			.GetQueryOptions();

		_ = result.Should().NotBeNull();
		_ = result!.TimeZone.Should().Be("UTC");
		_ = result.Locale.Should().Be("en-US");
	}

	[Test]
	public void WithOptions_CoreAndExecutorOptions_PopulateSeparateSlots()
	{
		var query = CreateQuery<LogEntry>()
			.WithOptions(new EsqlQueryOptions { TimeZone = "UTC" })
			.WithOptions(new TestQueryOptions(Locale: "de-DE"))
			.From("logs-*")
			.AsEsqlQueryable();

		_ = query.GetQueryOptions()!.TimeZone.Should().Be("UTC");
		_ = query.GetExecutorOptions().Should().BeOfType<TestQueryOptions>();
	}

	[Test]
	public void WithOptions_WithoutAttribute_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.WithOptions(new UnattributedQueryOptions(Name: "x"))
			.From("logs-*")
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("Method 'UnattributedQueryableExtensions.WithOptions' is not supported in ES|QL translation.");
	}

	[Test]
	public void WithOptions_SameSlotCalledTwice_ThrowsInvalidOperation()
	{
		var act = () => CreateQuery<LogEntry>()
			.WithOptions(new TestQueryOptions(TimeZone: "UTC"))
			.WithOptions(new TestQueryOptions(TimeZone: "America/New_York", Locale: "en-US"))
			.From("logs-*")
			.AsEsqlQueryable()
			.GetExecutorOptions();

		_ = act.Should().Throw<InvalidOperationException>()
			.WithMessage("Query options were already set earlier in this query chain*");
	}
}
