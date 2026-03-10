// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

public class NestedSubFieldSelectionTests : EsqlTestBase
{
	[Test]
	public void Select_NestedSubField_GeneratesKeepWithDottedPath()
	{
		var esql = CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => l.Host.Name)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP host.name
			""".NativeLineEndings());
	}

	[Test]
	public void Select_ObjectMember_GeneratesWildcardKeep()
	{
		var esql = CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => l.Host)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| KEEP host.*
			""".NativeLineEndings());
	}

	[Test]
	public void Select_ProjectionAlias_FromNestedSubField_GeneratesRenameAndKeep()
	{
		var esql = CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => new { HostName = l.Host.Name })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME host.name AS hostName
			| KEEP hostName
			""".NativeLineEndings());
	}

	[Test]
	public void Select_MixedFlatAndNestedFields_GeneratesRenameAndKeep()
	{
		var esql = CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => new { l.Message, HostName = l.Host.Name, l.Host.Geo.City })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME host.name AS hostName, host.geo.city AS city
			| KEEP message, hostName, city
			""".NativeLineEndings());
	}

	[Test]
	public void Select_SiblingNestedPaths_WithSameLeafName_GeneratesDistinctRenames()
	{
		var esql = CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => new { HostName = l.Host.Name, AgentName = l.Agent.Name })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| RENAME host.name AS hostName, agent.name AS agentName
			| KEEP hostName, agentName
			""".NativeLineEndings());
	}

	[Test]
	public void Select_ObjectAlias_ThrowsNotSupported()
	{
		var act = () => CreateQuery<NestedSelectionDocument>()
			.From("logs-*")
			.Select(l => new { Node = l.Host })
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*Aliasing object selections is not supported*");
	}
}
