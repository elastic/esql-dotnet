// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Elastic.Clients.Esql.Execution;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;

namespace Elastic.Clients.Esql;

// TODO: MappingFieldMetadataResolver signature prevents using a different resolver implementation.

/// <summary>
/// An <see cref="EsqlQueryProvider"/> that executes queries against Elasticsearch via <see cref="EsqlTransportExecutor"/>.
/// </summary>
public class EsqlClientQueryProvider(MappingFieldMetadataResolver resolver, EsqlTransportExecutor executor) : EsqlQueryProvider(resolver)
{
	/// <inheritdoc/>
	protected override TResult ExecuteCore<TResult>(Expression expression) =>
		ExecuteCoreAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();

	/// <inheritdoc/>
	protected override async Task<TResult> ExecuteCoreAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);
		var response = await executor.ExecuteAsync(esql, query.Parameters?.ToEsqlParams(), cancellationToken);
		var materializer = new ResultMaterializer(resolver.MappingResolver);

		var isScalarResult = GetElementType(expression) is null;
		if (isScalarResult)
		{
			// TODO: It would be nice, if we could keep this logic in the base EsqlQueryProvider to avoid implementing it multiple times.
			var methodName = expression is MethodCallExpression method ? method.Method.Name : string.Empty;
			switch (methodName)
			{
				case nameof(Queryable.First):
				case nameof(EsqlQueryableExtensions.FirstAsync):
					if (response.Values.Count == 0)
						_ = Array.Empty<int>().First();
					break;

				case nameof(Queryable.Single):
				case nameof(EsqlQueryableExtensions.SingleAsync):
					if (response.Values.Count is 0 or > 1)
						_ = Array.Empty<int>().Single();
					break;

				case nameof(Queryable.SingleOrDefault):
				case nameof(EsqlQueryableExtensions.SingleOrDefaultAsync):
					if (response.Values.Count > 1)
						_ = Array.Empty<int>().Single();
					break;

				case nameof(Queryable.FirstOrDefault):
				case nameof(EsqlQueryableExtensions.FirstOrDefaultAsync):
				case nameof(Queryable.Count):
				case nameof(EsqlQueryableExtensions.CountAsync):
				case nameof(Queryable.LongCount):
				case nameof(Queryable.Any):
				case nameof(EsqlQueryableExtensions.AnyAsync):
				case nameof(Queryable.Sum):
				case nameof(Queryable.Average):
				case nameof(Queryable.Min):
				case nameof(Queryable.Max):
					// Nothing to do here.
					break;

				default:
					throw new NotSupportedException($"Operation '{methodName}' is not supported.");
			}

			return materializer.MaterializeScalar<TResult>(response);
		}

		var results = materializer.Materialize<TResult>(response, query);
		return (TResult)(object)results.ToList();
	}

	/// <inheritdoc/>
	protected override async IAsyncEnumerable<TElement> ExecuteCoreStreamingAsync<TElement>(Expression expression,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);
		var response = await executor.ExecuteAsync(esql, query.Parameters?.ToEsqlParams(), cancellationToken);
		var materializer = new ResultMaterializer(resolver.MappingResolver);

		foreach (var item in materializer.Materialize<TElement>(response, query))
			yield return item;
	}

	private (string Esql, EsqlQuery Query) TranslateAndFormat(Expression expression)
	{
		var query = TranslateExpression(expression, false);
		var esql = new EsqlFormatter().Format(query);
		return (esql, query);
	}
}
