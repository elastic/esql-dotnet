// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.Generation;

public class IndexPatternFormattingTests
{
	private readonly EsqlFormatter _formatter = new();

	[Test]
	public void Format_FromWithWildcardPattern_EmitsVerbatim()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("logs-*")], null);

		_ = _formatter.Format(query).Should().Be("FROM logs-*");
	}

	[Test]
	public void Format_FromWithDateStylePattern_EmitsVerbatim()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("metrics-2024.01")], null);

		_ = _formatter.Format(query).Should().Be("FROM metrics-2024.01");
	}

	[Test]
	public void Format_FromWithCrossClusterPattern_EmitsVerbatim()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("cluster:logs-*")], null);

		_ = _formatter.Format(query).Should().Be("FROM cluster:logs-*");
	}

	[Test]
	public void Format_FromWithCommaSeparatedPatterns_EmitsVerbatim()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("logs-*,metrics-*")], null);

		_ = _formatter.Format(query).Should().Be("FROM logs-*,metrics-*");
	}

	[Test]
	public void Format_FromWithSpaceInName_EmitsDoubleQuoted()
	{
		var query = new EsqlQuery(typeof(object), [new FromCommand("my index")], null);

		_ = _formatter.Format(query).Should().Be("FROM \"my index\"");
	}

	[Test]
	public void Format_LookupJoinWithSpaceInIndex_EmitsDoubleQuoted()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new FromCommand("logs-*"),
			new LookupJoinCommand("my lookup", "message")
		], null);

		_ = _formatter.Format(query).Should().Be(
			"""
			FROM logs-*
			| LOOKUP JOIN "my lookup" ON message
			""".NativeLineEndings());
	}

	[Test]
	public void Format_RenameWithPreEscapedFields_EmitsVerbatim()
	{
		var query = new EsqlQuery(typeof(object),
		[
			new FromCommand("logs-*"),
			new RenameCommand([("`user-agent`", "ua")])
		], null);

		_ = _formatter.Format(query).Should().Be(
			"""
			FROM logs-*
			| RENAME `user-agent` AS ua
			""".NativeLineEndings());
	}
}
