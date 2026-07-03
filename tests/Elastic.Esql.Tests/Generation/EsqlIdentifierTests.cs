// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Formatting;

namespace Elastic.Esql.Tests.Generation;

public class EsqlIdentifierTests
{
	[Test]
	public void EscapeColumnName_PlainName_ReturnsUnchanged() =>
		_ = EsqlIdentifier.EscapeColumnName("message").Should().Be("message");

	[Test]
	public void EscapeColumnName_DottedPath_ReturnsUnchanged() =>
		_ = EsqlIdentifier.EscapeColumnName("log.level").Should().Be("log.level");

	[Test]
	public void EscapeColumnName_LeadingAt_ReturnsUnchanged() =>
		_ = EsqlIdentifier.EscapeColumnName("@timestamp").Should().Be("@timestamp");

	[Test]
	public void EscapeColumnName_LeadingUnderscore_ReturnsUnchanged() =>
		_ = EsqlIdentifier.EscapeColumnName("_id").Should().Be("_id");

	[Test]
	public void EscapeColumnName_Hyphen_QuotesSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("user-agent").Should().Be("`user-agent`");

	[Test]
	public void EscapeColumnName_Space_QuotesSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("os name").Should().Be("`os name`");

	[Test]
	public void EscapeColumnName_DottedPathWithSpecialSegments_QuotesPerSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("user-agent.os name").Should().Be("`user-agent`.`os name`");

	[Test]
	public void EscapeColumnName_MixedPath_QuotesOnlySpecialSegments() =>
		_ = EsqlIdentifier.EscapeColumnName("user-agent.version").Should().Be("`user-agent`.version");

	[Test]
	public void EscapeColumnName_EmbeddedBacktick_DoublesBacktick() =>
		_ = EsqlIdentifier.EscapeColumnName("weird`name").Should().Be("`weird``name`");

	[Test]
	public void EscapeColumnName_LeadingDigit_QuotesSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("1field").Should().Be("`1field`");

	[Test]
	public void EscapeColumnName_MidStringAt_QuotesSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("user@domain").Should().Be("`user@domain`");

	[Test]
	public void EscapeColumnName_ReservedKeyword_QuotesSegment() =>
		_ = EsqlIdentifier.EscapeColumnName("like").Should().Be("`like`");
}
