// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Elastic.Clients.Esql.Execution;
using Elastic.Esql.Core;
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
	public override async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);
		var response = await executor.ExecuteAsync(esql, query.Parameters?.ToEsqlParams(), cancellationToken);
		var materializer = new ResultMaterializer(resolver.MappingResolver);

		if (IsScalarResult(expression))
			return materializer.MaterializeScalar<TResult>(response);

		var elementType = GetElementType(expression.Type) ?? typeof(TResult);
		var results = materializer.Materialize<TResult>(response, query);
		return (TResult)(object)results.ToList();
	}

	/// <inheritdoc/>
	public override async IAsyncEnumerable<T> ExecuteStreamingAsync<T>(Expression expression, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);
		var response = await executor.ExecuteAsync(esql, query.Parameters?.ToEsqlParams(), cancellationToken);
		var materializer = new ResultMaterializer(resolver.MappingResolver);

		foreach (var item in materializer.Materialize<T>(response, query))
			yield return item;
	}

	private (string Esql, EsqlQuery Query) TranslateAndFormat(Expression expression)
	{
		var query = TranslateExpression(expression, false);
		var esql = new EsqlFormatter().Format(query);
		return (esql, query);
	}
}
