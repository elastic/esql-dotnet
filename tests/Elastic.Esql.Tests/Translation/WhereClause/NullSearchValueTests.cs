// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.WhereClause;

/// <summary>A null search value must fail translation instead of degrading to a match-all LIKE pattern.</summary>
public class NullSearchValueTests : EsqlTestBase
{
	[Test]
	public void Where_ContainsNullCapture_ThrowsNotSupported()
	{
		var term = (string?)null;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.Contains(term!))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*Contains*must not be null*");
	}

	[Test]
	public void Where_StartsWithNullCapture_ThrowsNotSupported()
	{
		var prefix = (string?)null;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.StartsWith(prefix!))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*StartsWith*must not be null*");
	}

	[Test]
	public void Where_EndsWithNullCapture_ThrowsNotSupported()
	{
		var suffix = (string?)null;

		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.Message.EndsWith(suffix!))
			.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*EndsWith*must not be null*");
	}
}
