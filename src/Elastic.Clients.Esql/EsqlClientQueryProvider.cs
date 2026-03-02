// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Esql.Execution;
using Elastic.Esql;
using Elastic.Esql.Core;
using Elastic.Esql.Extensions;
using Elastic.Esql.FieldMetadataResolver;
using Elastic.Esql.Generation;
using Elastic.Esql.Materialization;
using Elastic.Esql.QueryModel;

namespace Elastic.Clients.Esql;

/// <summary>
/// An <see cref="EsqlQueryProvider"/> that executes queries against Elasticsearch via <see cref="EsqlTransportExecutor"/>.
/// Results are materialized directly from the HTTP response stream using <c>System.Text.Json</c>.
/// </summary>
public class EsqlClientQueryProvider : EsqlQueryProvider
{
	private readonly EsqlTransportExecutor _executor;
	private readonly JsonSerializerOptions _jsonOptions;

	/// <summary>Creates a provider with an executor and explicit <see cref="JsonSerializerOptions"/>.</summary>
	internal EsqlClientQueryProvider(EsqlTransportExecutor executor, JsonSerializerOptions? options = null)
		: base(new SystemTextJsonFieldNameResolver(options ?? JsonSerializerOptions.Default))
	{
		_executor = executor ?? throw new ArgumentNullException(nameof(executor));
		_jsonOptions = options ?? JsonSerializerOptions.Default;
	}

	/// <inheritdoc/>
	protected override TResult ExecuteCore<TResult>(Expression expression) =>
		ExecuteCoreAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();

	/// <inheritdoc/>
	protected override async Task<TResult> ExecuteCoreAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);

		var isScalarResult = GetElementType(expression) is null;
		if (isScalarResult)
			return await ExecuteScalarAsync<TResult>(esql, query, expression, cancellationToken).ConfigureAwait(false);

		return await ExecuteCollectionAsync<TResult>(esql, query, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	protected override async IAsyncEnumerable<TElement> ExecuteCoreStreamingAsync<TElement>(
		Expression expression,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var (esql, query) = TranslateAndFormat(expression);

#if NET10_0_OR_GREATER
		await using var response = await _executor
			.ExecuteStreamingAsync(esql, ToOptions(query), cancellationToken)
			.ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<TElement>(response.Body, _jsonOptions, cancellationToken).ConfigureAwait(false))
			yield return item;
#else
		using var response = await _executor
			.ExecuteStreamingAsync(esql, ToOptions(query), cancellationToken)
			.ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<TElement>(response.Body, _jsonOptions, cancellationToken).ConfigureAwait(false))
			yield return item;
#endif
	}

	private async Task<TResult> ExecuteScalarAsync<TResult>(
		string esql,
		EsqlQuery query,
		Expression expression,
		CancellationToken cancellationToken)
	{
#if NET10_0_OR_GREATER
		await using var response = await _executor
			.ExecuteStreamingAsync(esql, ToOptions(query), cancellationToken)
			.ConfigureAwait(false);

		var result = await EsqlResponseReader.ReadScalarAsync<TResult>(response.Body, _jsonOptions, cancellationToken)
			.ConfigureAwait(false);
#else
		using var response = await _executor.ExecuteStreamingAsync(
			esql, ToOptions(query), cancellationToken).ConfigureAwait(false);

		var result = await EsqlResponseReader.ReadScalarAsync<TResult>(response.Body, _jsonOptions, cancellationToken)
			.ConfigureAwait(false);
#endif

		ValidateScalarCardinality(expression, result.RowCount);
		return result.Value!;
	}

	private async Task<TResult> ExecuteCollectionAsync<TResult>(
		string esql,
		EsqlQuery query,
		CancellationToken cancellationToken)
	{
		var list = new List<TResult>();

#if NET10_0_OR_GREATER
		await using var response = await _executor
			.ExecuteStreamingAsync(esql, ToOptions(query), cancellationToken)
			.ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<TResult>(response.Body, _jsonOptions, cancellationToken).ConfigureAwait(false))
			list.Add(item);
#else
		using var response = await _executor
			.ExecuteStreamingAsync(esql, ToOptions(query), cancellationToken)
			.ConfigureAwait(false);

		await foreach (var item in EsqlResponseReader.ReadRowsAsync<TResult>(response.Body, _jsonOptions, cancellationToken).ConfigureAwait(false))
			list.Add(item);
#endif

		return (TResult)(object)list;
	}

	private static void ValidateScalarCardinality(Expression expression, int rowCount)
	{
		var methodName = expression is MethodCallExpression method ? method.Method.Name : string.Empty;

		switch (methodName)
		{
			case nameof(Queryable.First):
			case nameof(EsqlQueryableExtensions.FirstAsync):
				if (rowCount == 0)
					throw new InvalidOperationException("Sequence contains no elements");
				break;

			case nameof(Queryable.Single):
			case nameof(EsqlQueryableExtensions.SingleAsync):
				if (rowCount == 0)
					throw new InvalidOperationException("Sequence contains no elements");
				if (rowCount > 1)
					throw new InvalidOperationException("Sequence contains more than one element");
				break;

			case nameof(Queryable.SingleOrDefault):
			case nameof(EsqlQueryableExtensions.SingleOrDefaultAsync):
				if (rowCount > 1)
					throw new InvalidOperationException("Sequence contains more than one element");
				break;

			case nameof(Queryable.FirstOrDefault):
			case nameof(EsqlQueryableExtensions.FirstOrDefaultAsync):
				break;

			case nameof(Queryable.Count):
			case nameof(EsqlQueryableExtensions.CountAsync):
			case nameof(Queryable.LongCount):
			case nameof(Queryable.Any):
			case nameof(EsqlQueryableExtensions.AnyAsync):
			case nameof(Queryable.Sum):
			case nameof(Queryable.Average):
			case nameof(Queryable.Min):
			case nameof(Queryable.Max):
				if (rowCount != 1)
					throw new InvalidOperationException($"Operation '{methodName}' expected exactly one row but got {rowCount}");
				break;

			default:
				throw new NotSupportedException($"Operation '{methodName}' is not supported.");
		}
	}

	private (string Esql, EsqlQuery Query) TranslateAndFormat(Expression expression)
	{
		var query = TranslateExpression(expression, false);
		var esql = new EsqlFormatter().Format(query);
		return (esql, query);
	}

	private static EsqlQueryOptions? ToOptions(EsqlQuery query) =>
		query.Parameters is { } p ? new EsqlQueryOptions { Parameters = p.ToEsqlParams() } : null;
}
