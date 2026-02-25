// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class LookupJoinTests : EsqlTestBase
{
	// ============================================================================
	// LookupJoin with key selectors (string index name)
	// ============================================================================

	[Test]
	public void LookupJoin_WithKeySelectors_DifferentFieldNames_GeneratesEqualityOn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_WithKeySelectors_MatchingFieldName_GeneratesSimpleOn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("firewall_logs")
			.LookupJoin<LogEntry, ThreatListEntry, string?, object>(
				"threat_list",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { outer.Message, inner!.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM firewall_logs
			| LOOKUP JOIN threat_list ON clientIp
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	// ============================================================================
	// LookupJoin with expression predicate (string index name)
	// ============================================================================

	[Test]
	public void LookupJoin_WithPredicate_EqualityCondition_GeneratesLookupJoin()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, object>(
				"languages_lookup",
				(outer, inner) => outer.StatusCode == inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_WithPredicate_ComparisonCondition_GeneratesExpressionOn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, object>(
				"languages_lookup",
				(outer, inner) => outer.StatusCode >= inner.LanguageCode,
				(outer, inner) => new { outer.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode >= languageCode
			| KEEP message
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_WithPredicate_CompoundCondition_GeneratesAndOn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, ThreatListEntry, object>(
				"threat_list",
				(outer, inner) => outer.ClientIp == inner.ClientIp && outer.Message == inner.ThreatLevel,
				(outer, inner) => new { outer.Message, inner!.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN threat_list ON (clientIp == clientIp AND message == threatLevel)
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_WithPredicate_MatchFunction_GeneratesMatchOn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, ThreatListEntry, object>(
				"threat_list",
				(outer, inner) => EsqlFunctions.Match(inner.ThreatLevel, "critical") && outer.ClientIp == inner.ClientIp,
				(outer, inner) => new { outer.Message, inner!.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN threat_list ON (MATCH(threatLevel, "critical") AND clientIp == clientIp)
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	// ============================================================================
	// LeftJoin with key selectors (IEnumerable<TInner>)
	// ============================================================================

	[Test]
	public void LeftJoin_WithKeySelectors_FromQueryable_GeneratesLookupJoin()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LeftJoin(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void LeftJoin_WithKeySelectors_MatchingFieldName_GeneratesSimpleOn()
	{
		var lookup = CreateQuery<ThreatListEntry>().From("threat_list");

		var esql = CreateQuery<LogEntry>()
			.From("firewall_logs")
			.LeftJoin(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { outer.Message, inner!.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM firewall_logs
			| LOOKUP JOIN threat_list ON clientIp
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	// ============================================================================
	// LeftJoin with expression predicate (IEnumerable<TInner>)
	// ============================================================================

	[Test]
	public void LeftJoin_WithPredicate_FromQueryable_GeneratesLookupJoin()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LeftJoin(
				lookup,
				(outer, inner) => outer.StatusCode == inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	// ============================================================================
	// Complex projection (EVAL) in result selector
	// ============================================================================

	[Test]
	public void LookupJoin_WithComplexProjection_GeneratesEvalAndKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { Msg = outer.Message, Lang = inner!.LanguageName.ToUpperInvariant() }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| EVAL msg = message, lang = TO_UPPER(languageName)
			| KEEP msg, lang
			""".NativeLineEndings());
	}

	// ============================================================================
	// Identity result selector (no KEEP generated)
	// ============================================================================

	[Test]
	public void LookupJoin_WithIdentitySelector_GeneratesNoKeep()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, LogEntry>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => outer
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			""".NativeLineEndings());
	}

	// ============================================================================
	// Combinations with other commands
	// ============================================================================

	[Test]
	public void LookupJoin_AfterWhere_GeneratesCorrectPipeline()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.Where(l => l.StatusCode >= 10091)
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| WHERE statusCode >= 10091
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_WithExplicitKeepAfterProjection_GeneratesBothKeeps()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.Keep("message")
			.Take(10)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			| KEEP message
			| LIMIT 10
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_MultipleLookupJoins_GeneratesChainedJoins()
	{
		var esql = CreateQuery<LogEntry>()
			.From("system_metrics")
			.LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
				"host_inventory",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => outer
			)
			.LookupJoin<LogEntry, ThreatListEntry, string?, LogEntry>(
				"ownerships",
				outer => outer.ServerName,
				inner => inner.ClientIp,
				(outer, inner) => outer
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM system_metrics
			| LOOKUP JOIN host_inventory ON clientIp
			| LOOKUP JOIN ownerships ON serverName == clientIp
			""".NativeLineEndings());
	}

	// ============================================================================
	// Validation
	// ============================================================================

	[Test]
	public void LeftJoin_WithQueryableWithoutFrom_ThrowsNotSupported()
	{
		var lookup = CreateQuery<LanguageLookup>();

		var act = () => CreateQuery<LogEntry>()
			.From("employees")
			.LeftJoin(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void LookupJoin_WithJsonPropertyName_ResolvesFieldNames()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, LanguageLookup, object>(
				"languages_lookup",
				(outer, inner) => outer.IsError,
				(outer, inner) => new { outer.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN languages_lookup ON isError
			| KEEP message
			""".NativeLineEndings());
	}
}
