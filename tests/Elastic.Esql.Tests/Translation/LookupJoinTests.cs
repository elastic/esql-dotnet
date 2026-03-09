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
			| RENAME message AS msg
			| EVAL lang = TO_UPPER(languageName)
			| KEEP msg, lang
			""".NativeLineEndings());
	}

	// ============================================================================
	// Nullable patterns in result selector
	// ============================================================================

	[Test]
	public void LookupJoin_NullGuard_ReferenceType_UnwrapsToField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, LanguageName = inner == null ? null : inner.LanguageName }
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
	public void LookupJoin_NullGuard_Inverted_UnwrapsToField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, LanguageName = inner != null ? inner.LanguageName : null }
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
	public void LookupJoin_NullGuard_ValueType_UnwrapsToField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, LanguageCode = inner == null ? (int?)null : inner.LanguageCode }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageCode
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_NullableCast_ValueType_UnwrapsToField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, LanguageCode = (int?)inner!.LanguageCode }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageCode
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_NullGuard_ComplexExpression_GeneratesCaseWhen()
	{
		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, Lang = inner == null ? null : inner.LanguageName.ToUpperInvariant() }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| EVAL lang = CASE WHEN languageName IS NOT NULL THEN TO_UPPER(languageName) ELSE NULL END
			| KEEP message, lang
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

	// ============================================================================
	// Queryable.Join (standard LINQ inner join syntax)
	// ============================================================================

	[Test]
	public void Join_WithKeySelectors_DifferentFieldNames_GeneratesWhereNotNull()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("employees")
			.Join(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| WHERE languageCode IS NOT NULL
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void Join_WithKeySelectors_MatchingFieldName_GeneratesWhereNotNull()
	{
		var lookup = CreateQuery<ThreatListEntry>().From("threat_list");

		var esql = CreateQuery<LogEntry>()
			.From("firewall_logs")
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { outer.Message, inner.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM firewall_logs
			| LOOKUP JOIN threat_list ON clientIp
			| WHERE clientIp IS NOT NULL
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	[Test]
	public void Join_WithKeySelectors_MatchingFieldName_WithRename_GeneratesCorrectPipeline()
	{
		var lookup = CreateQuery<ThreatListEntry>().From("threat_list");

		var esql = CreateQuery<LogEntry>()
			.From("firewall_logs")
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { Msg = outer.Message, Threat = inner.ThreatLevel }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM firewall_logs
			| LOOKUP JOIN threat_list ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME message AS msg, threatLevel AS threat
			| KEEP msg, threat
			""".NativeLineEndings());
	}

	// ============================================================================
	// GroupJoin + SelectMany (LINQ query syntax left outer join)
	// ============================================================================

	[Test]
	public void QuerySyntax_LeftOuterJoin_GeneratesLookupJoin()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees")
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			from inner in ps.DefaultIfEmpty()
			select new { outer.Message, inner!.LanguageName }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_MatchingKey_GeneratesSimpleOn()
	{
		var lookup = CreateQuery<ThreatListEntry>().From("threat_list");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("firewall_logs")
			join inner in lookup on outer.ClientIp equals inner.ClientIp into ps
			from inner in ps.DefaultIfEmpty()
			select new { outer.Message, inner!.ThreatLevel }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM firewall_logs
			| LOOKUP JOIN threat_list ON clientIp
			| KEEP message, threatLevel
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_WithRename_GeneratesRenameAndKeep()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees")
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			from inner in ps.DefaultIfEmpty()
			select new { Msg = outer.Message, inner!.LanguageName }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| RENAME message AS msg
			| KEEP languageName, msg
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_WithEval_GeneratesEvalAndKeep()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees")
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			from inner in ps.DefaultIfEmpty()
			select new { Msg = outer.Message, Lang = inner!.LanguageName.ToUpperInvariant() }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| RENAME message AS msg
			| EVAL lang = TO_UPPER(languageName)
			| KEEP msg, lang
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_WithWhere_GeneratesCorrectPipeline()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees").Where(l => l.StatusCode >= 10091)
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			from inner in ps.DefaultIfEmpty()
			select new { outer.Message, inner!.LanguageName }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| WHERE statusCode >= 10091
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_WithNullGuard_UnwrapsToField()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

#pragma warning disable IDE0031 // null-coalescing not supported in expression trees
		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees")
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			from inner in ps.DefaultIfEmpty()
			select new { outer.Message, LanguageName = inner == null ? null : inner.LanguageName }
		).ToString();
#pragma warning restore IDE0031

		_ = esql.Should().Be(
			"""
			FROM employees
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_WithWhereBetweenGroupJoinAndSelectMany_GeneratesCorrectPipeline()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("employees")
			join inner in lookup on outer.StatusCode equals inner.LanguageCode into ps
			where outer.StatusCode >= 10091
			from inner in ps.DefaultIfEmpty()
			select new { outer.Message, inner!.LanguageName }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM employees
			| WHERE statusCode >= 10091
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	// ============================================================================
	// GroupJoin + SelectMany — validation
	// ============================================================================

	[Test]
	public void QuerySyntax_SelectManyWithoutGroupJoin_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("employees")
			.SelectMany(l => new[] { l.Message, l.ClientIp! })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void QuerySyntax_GroupJoinWithoutSelectMany_ThrowsNotSupported()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var act = () => CreateQuery<LogEntry>()
			.From("employees")
			.GroupJoin(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, ps) => new { outer, ps }
			)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void QuerySyntax_ConsecutiveGroupJoinsWithoutSelectMany_ThrowsNotSupported()
	{
		var lookup1 = CreateQuery<LanguageLookup>().From("languages_lookup");
		var lookup2 = CreateQuery<ThreatListEntry>().From("threat_list");

		var act = () => CreateQuery<LogEntry>()
			.From("employees")
			.GroupJoin(
				lookup1,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, ps) => new { outer, ps }
			)
			.GroupJoin(
				lookup2,
				x => x.outer.ClientIp,
				inner => inner.ClientIp,
				(x, ps2) => new { x, ps2 }
			)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	// ============================================================================
	// Field name collision handling
	// ============================================================================

	[Test]
	public void LookupJoin_Collision_BothOuterAndInnerProjected_GeneratesEvalAndRemapping()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner!.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_OnlyOuterProjected_RemapsOuterField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { Msg = outer.Message, inner!.Region }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS msg
			| KEEP region, msg
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_OnlyInnerProjected_NoEvalNeeded()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { inner!.Message, inner.Region }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN host_lookup ON clientIp
			| KEEP message, region
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_OuterFieldKeptWithOriginalName_RenamesBackFromTemp()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { outer.Message, inner!.Region }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| EVAL message = _esql_outer_message
			| KEEP region, message
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_ComplexExpression_UsesRemappedFieldInEval()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message.ToUpperInvariant(), inner!.Region }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| EVAL outerMsg = TO_UPPER(_esql_outer_message)
			| KEEP region, outerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_NoCollision_NoEvalGenerated()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, LanguageLookup, int, object>(
				"languages_lookup",
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner!.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void Join_Collision_BothOuterAndInnerProjected_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void Join_Collision_DifferentKeys_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Join(
				lookup,
				outer => outer.ServerName,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON serverName == clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void QuerySyntax_LeftOuterJoin_Collision_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = (
			from outer in CreateQuery<LogEntry>().From("logs-*")
			join inner in lookup on outer.ClientIp equals inner.ClientIp into ps
			from inner in ps.DefaultIfEmpty()
			select new { OuterMsg = outer.Message, InnerMsg = inner!.Message }
		).ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void LeftJoin_Collision_WithKeySelectors_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LeftJoin(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner!.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_NullGuard_RemapsOuterField()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, object>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner == null ? null : inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_IdentitySelector_NoEvalGenerated()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, LogEntry>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => outer
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN host_lookup ON clientIp
			""".NativeLineEndings());
	}

	[Test]
	public void LookupJoin_Collision_PredicateVariant_GeneratesEvalAndRemapping()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, object>(
				"host_lookup",
				(outer, inner) => outer.ClientIp == inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner!.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp == clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	// ============================================================================
	// Anonymous outer type (Select before Join)
	// ============================================================================

	[Test]
	public void Join_AnonymousOuter_Collision_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.ClientIp })
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, clientIp
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void Join_AnonymousOuter_NoCollision_NoEvalGenerated()
	{
		var lookup = CreateQuery<LanguageLookup>().From("languages_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.StatusCode })
			.Join(
				lookup,
				outer => outer.StatusCode,
				inner => inner.LanguageCode,
				(outer, inner) => new { outer.Message, inner.LanguageName }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, statusCode
			| LOOKUP JOIN languages_lookup ON statusCode == languageCode
			| WHERE languageCode IS NOT NULL
			| KEEP message, languageName
			""".NativeLineEndings());
	}

	[Test]
	public void Join_AnonymousOuter_WithRename_Collision_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Message = l.Level, l.ClientIp })
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME log.level AS message
			| KEEP clientIp, message
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void Join_AnonymousOuter_WithEval_Collision_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { Message = l.Level.ToUpperInvariant(), l.ClientIp })
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL message = TO_UPPER(log.level)
			| KEEP clientIp, message
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	[Test]
	public void Join_TwoSelectsBeforeJoin_Collision_GeneratesEvalAndRemapping()
	{
		var lookup = CreateQuery<OverlappingLookup>().From("host_lookup");

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => new { l.Message, l.ClientIp, l.StatusCode })
			.Select(x => new { x.Message, x.ClientIp })
			.Join(
				lookup,
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new { OuterMsg = outer.Message, InnerMsg = inner.Message }
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP message, clientIp
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| WHERE clientIp IS NOT NULL
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}

	// ============================================================================
	// Constructor-call projections with collision handling
	// ============================================================================

	[Test]
	public void LookupJoin_Collision_RecordConstructor_GeneratesEvalAndRemapping()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.LookupJoin<LogEntry, OverlappingLookup, string?, CollisionRecord>(
				"host_lookup",
				outer => outer.ClientIp,
				inner => inner.ClientIp,
				(outer, inner) => new CollisionRecord(outer.Message, inner!.Message)
			)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| EVAL _esql_outer_message = message
			| LOOKUP JOIN host_lookup ON clientIp
			| RENAME _esql_outer_message AS outerMsg, message AS innerMsg
			| KEEP outerMsg, innerMsg
			""".NativeLineEndings());
	}
}
