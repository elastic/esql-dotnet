// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;

namespace Elastic.Esql.Tests.Execution;

public class AsyncQueryOptionValidationTests
{
	[Test]
	public void SubmitAsyncQuery_WithNonPositivePollInterval_ThrowsArgumentOutOfRange()
	{
		var provider = CreateProvider();
		var query = new EsqlQueryable<LogEntry>(provider).From("logs-*");

		var act = () => provider.SubmitAsyncQuery<LogEntry>(query.Expression, new EsqlAsyncQueryOptions { PollInterval = TimeSpan.Zero });

		_ = act.Should().Throw<ArgumentOutOfRangeException>()
			.WithMessage("*PollInterval*");
	}

	[Test]
	public async Task SubmitAsyncQueryAsync_WithNonPositivePollInterval_ThrowsArgumentOutOfRange()
	{
		var provider = CreateProvider();
		var query = new EsqlQueryable<LogEntry>(provider).From("logs-*");

		var act = async () =>
		{
			_ = await provider.SubmitAsyncQueryAsync<LogEntry>(
				query.Expression,
				new EsqlAsyncQueryOptions { PollInterval = TimeSpan.Zero },
				default);
		};

		_ = await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
			.WithMessage("*PollInterval*");
	}

	private static EsqlQueryProvider CreateProvider() =>
		new(new JsonSerializerOptions(JsonSerializerDefaults.Web), ThrowingQueryExecutor.Instance);
}
