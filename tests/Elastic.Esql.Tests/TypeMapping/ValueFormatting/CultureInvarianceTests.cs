// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
using Elastic.Esql.Formatting;

namespace Elastic.Esql.Tests.TypeMapping.ValueFormatting;

/// <summary>
/// Pins generated ES|QL to invariant formatting by running translations under cultures whose
/// separators or calendar differ from the invariant culture. CurrentCulture is per-thread and
/// each assertion runs synchronously on the thread that set it, so these tests are safe under
/// TUnit's parallel execution; the culture is restored in a finally block either way.
/// </summary>
public class CultureInvarianceTests : EsqlTestBase
{
	private static void RunWithCulture(string cultureName, Action assertion)
	{
		var culture = new CultureInfo(cultureName);
		var previousCulture = CultureInfo.CurrentCulture;
		var previousUiCulture = CultureInfo.CurrentUICulture;

		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
		try
		{
			assertion();
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
			CultureInfo.CurrentUICulture = previousUiCulture;
		}
	}

	[Test]
	public void Where_DateTimeConstant_UnderFinnishCulture_EmitsInvariantTimeSeparators() =>
		RunWithCulture("fi-FI", () =>
		{
			var cutoff = new DateTime(2024, 1, 15, 10, 30, 45, 123, DateTimeKind.Utc);

			var esql = CreateQuery<LogEntry>()
				.From("logs-*")
				.Where(l => l.Timestamp >= cutoff)
				.ToString();

			_ = esql.Should().Be(
				"""
				FROM logs-*
				| WHERE @timestamp >= "2024-01-15T10:30:45.123Z"
				""".NativeLineEndings());
		});

	[Test]
	public void FormatValue_TimeOnly_UnderFinnishCulture_EmitsInvariantTimeSeparators() =>
		RunWithCulture("fi-FI", () =>
		{
			var result = EsqlFormatting.FormatValue(new TimeOnly(10, 30, 45), ReaderOptions);

			_ = result.Should().Be("\"10:30:45\"");
		});

	[Test]
	public void FormatValue_DateOnly_UnderThaiCulture_EmitsGregorianYear() =>
		RunWithCulture("th-TH", () =>
		{
			var result = EsqlFormatting.FormatValue(new DateOnly(2024, 1, 15), ReaderOptions);

			_ = result.Should().Be("\"2024-01-15\"");
		});

	[Test]
	public void Where_DoubleConstant_UnderGermanCulture_EmitsInvariantDecimalSeparator() =>
		RunWithCulture("de-DE", () =>
		{
			var esql = CreateQuery<LogEntry>()
				.From("logs-*")
				.Where(l => l.Duration > 3.14)
				.ToString();

			_ = esql.Should().Be(
				"""
				FROM logs-*
				| WHERE duration > 3.14
				""".NativeLineEndings());
		});

	[Test]
	public void FormatValue_FractionalTimeSpan_UnderGermanCulture_EmitsInvariantDecimalSeparator() =>
		RunWithCulture("de-DE", () =>
		{
			// 15005000 ticks = 1500.5 ms, forcing the fractional-milliseconds branch.
			var result = EsqlFormatting.FormatValue(TimeSpan.FromTicks(15005000), ReaderOptions);

			_ = result.Should().Be("1500.5 milliseconds");
		});
}
