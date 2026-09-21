// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NullCheckTests : EsqlTestBase
{
	[Test]
	public void Where_EqualsNull_GeneratesComparison()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.ClientIp == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE clientIp IS NULL
            """.NativeLineEndings());
	}

	[Test]
	public void Where_NotEqualsNull_GeneratesIsNotNull()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.ClientIp != null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE clientIp IS NOT NULL
            """.NativeLineEndings());
	}

	[Test]
	public void ANullGuardOnTheDocumentItself_IsAConstant()
	{
		// generated predicates guard the root against null; a document never is,
		// and there is no field name to put in front of IS NOT NULL
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l != null && l.ClientIp != null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE (TRUE AND clientIp IS NOT NULL)
            """.NativeLineEndings());
	}

	[Test]
	public void ADocumentComparedToNull_IsFalse()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l == null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE FALSE
            """.NativeLineEndings());
	}

	[Test]
	public void ACapturedNullRootGuard_IsAlsoAConstant()
	{
		var missing = (LogEntry?)null;

		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l != missing)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TRUE
            """.NativeLineEndings());
	}

	[Test]
	public void KeepDoesNotCountAsAProjection()
	{
		// Keep narrows the columns but the rows are still documents, so the guard on the
		// document parameter still folds to a constant
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Keep("message")
			.Where(l => l != null)
			.ToString();

		_ = esql.Should().Contain("WHERE TRUE");
	}

	[Test]
	public void AProjectedRow_IsStillProjectedInsideAForkBranch()
	{
		// the branch starts a fresh context: the parent's projection has to carry over, or
		// the guard on the projected row folds to FALSE and empties the branch
		var query = CreateQuery<TreeNode>()
			.From("nodes")
			.Select(n => n.Child)
			.Fork(b => b.Where(n => n == null), b => b.Take(1));

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>();
	}

	[Test]
	public void AnIdentitySelect_LeavesTheDocumentRowInPlace()
	{
		// Select(l => l) emits nothing and hands the row back as it is, so the guard on
		// the document parameter is still a constant afterwards
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Select(l => l)
			.Where(l => l != null)
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM logs-*
            | WHERE TRUE
            """.NativeLineEndings());
	}
}
