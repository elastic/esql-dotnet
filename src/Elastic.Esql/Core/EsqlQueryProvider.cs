// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Execution;
using Elastic.Esql.Extensions;
using Elastic.Esql.Generation;
using Elastic.Esql.Materialization;
using Elastic.Esql.QueryModel;
using Elastic.Esql.Translation;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Core;

/// <summary>
/// The ES|QL query provider. Translates LINQ expressions to ES|QL and executes them
/// via an <see cref="IEsqlQueryExecutor"/> when one is supplied.
/// </summary>
public sealed class EsqlQueryProvider : IQueryProvider
{
	private readonly IEsqlQueryExecutor _executor;
	private readonly EsqlResponseReader _reader;

	/// <summary>Centralized STJ metadata manager with provider-scoped caching.</summary>
	internal JsonMetadataManager Metadata { get; }

	/// <summary>Creates a translation-only provider using default camelCase JSON options.</summary>
	public EsqlQueryProvider() : this(CreateDefaultJsonOptions()) { }

	/// <summary>Creates a translation-only provider using the provided <see cref="JsonSerializerOptions"/>.</summary>
	public EsqlQueryProvider(JsonSerializerOptions options)
		: this(options, ThrowingQueryExecutor.Instance) { }

	/// <summary>Creates a translation-only provider from a source-generated <see cref="JsonSerializerContext"/>.</summary>
	public EsqlQueryProvider(JsonSerializerContext context)
		: this(ResolveOptions(context), ThrowingQueryExecutor.Instance) { }

	/// <summary>Creates an execution-capable provider using the provided <see cref="JsonSerializerOptions"/> and executor.</summary>
	public EsqlQueryProvider(JsonSerializerOptions options, IEsqlQueryExecutor executor)
	{
		Verify.NotNull(options);
		Verify.NotNull(executor);

		Metadata = new JsonMetadataManager(options);
		_reader = new EsqlResponseReader(Metadata);
		_executor = executor;
	}

	/// <summary>Creates an execution-capable provider from a source-generated <see cref="JsonSerializerContext"/> and executor.</summary>
	public EsqlQueryProvider(JsonSerializerContext context, IEsqlQueryExecutor executor)
		: this(ResolveOptions(context), executor) { }

	/// <inheritdoc/>
	public IQueryable CreateQuery(Expression expression)
	{
		Verify.NotNull(expression);

		var elementType = GetElementType(expression)
						  ?? throw new ArgumentException("Expression does not represent a queryable sequence.", nameof(expression));

		var queryableType = typeof(EsqlQueryable<>).MakeGenericType(elementType);

		try
		{
			var instance = Activator.CreateInstance(queryableType, this, expression);
			if (instance is IQueryable queryable)
				return queryable;

			throw new InvalidOperationException($"Unable to create queryable instance for '{queryableType.FullName}'.");
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw; // unreachable
		}
	}

	/// <inheritdoc/>
	public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
	{
		Verify.NotNull(expression);

		return new EsqlQueryable<TElement>(this, expression);
	}

	/// <inheritdoc/>
	public object? Execute(Expression expression)
	{
		Verify.NotNull(expression);

		return ExecuteCore<object?>(expression);
	}

	/// <inheritdoc/>
	public TResult Execute<TResult>(Expression expression)
	{
		Verify.NotNull(expression);

		return ExecuteCore<TResult>(expression);
	}

	/// <summary>Asynchronously executes a scalar query represented by a specified expression tree.</summary>
	internal async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		var (esql, query) = TranslateAndFormat(expression);
		var options = query.Parameters is { } p ? new EsqlQueryOptions { Parameters = p.ToEsqlParams() } : null;

		await using var response = await _executor
			.ExecuteQueryAsync(esql, options, cancellationToken)
			.ConfigureAwait(false);

		var result = await _reader.ReadScalarAsync<TResult>(response.Body, cancellationToken)
			.ConfigureAwait(false);

		ValidateScalarCardinality(expression, result.RowCount);
		return result.Value!;
	}

	/// <summary>Executes the specified query expression asynchronously and returns the results as a stream of elements.</summary>
	internal async IAsyncEnumerable<TElement> ExecuteStreamingAsync<TElement>(
		Expression expression,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		var elementType = GetElementType(expression);
		if (elementType is null || elementType != typeof(TElement))
			throw new ArgumentException($"Expression must return a queryable of '{typeof(TElement).Name}' elements.", nameof(expression));

		var (esql, query) = TranslateAndFormat(expression);
		var options = query.Parameters is { } p ? new EsqlQueryOptions { Parameters = p.ToEsqlParams() } : null;

		await using var response = await _executor
			.ExecuteQueryAsync(esql, options, cancellationToken)
			.ConfigureAwait(false);

		await foreach (var item in _reader.ReadRowsAsync<TElement>(response.Body, cancellationToken).ConfigureAwait(false))
			yield return item;
	}

	/// <summary>Translates the specified LINQ expression into an equivalent ES|QL query representation.</summary>
	internal EsqlQuery TranslateExpression(Expression expression, bool inlineParameters)
	{
		Verify.NotNull(expression);

		var visitor = new EsqlExpressionVisitor(this, null /* TODO: Implement */, inlineParameters);

		return visitor.Translate(expression);
	}

	/// <summary>Submits an async ES|QL query from a LINQ expression. Used by extension methods.</summary>
	internal EsqlAsyncQuery<T> SubmitAsyncQuery<T>(Expression expression, EsqlAsyncQueryOptions? asyncOptions)
	{
		var pollInterval = ResolvePollInterval(asyncOptions);
		var (esql, query) = TranslateAndFormat(expression);
		var options = query.Parameters is { } p
			? (asyncOptions ?? new EsqlAsyncQueryOptions()) with { Parameters = p.ToEsqlParams() }
			: asyncOptions;
		var response = _executor.SubmitAsyncQuery(esql, options);
		var result = _reader.ReadRowsWithMetadata<T>(response.Body);
		return new EsqlAsyncQuery<T>(_executor, result, response, _reader, pollInterval);
	}

	/// <summary>Submits an async ES|QL query from a LINQ expression asynchronously. Used by extension methods.</summary>
	internal async Task<EsqlAsyncQuery<T>> SubmitAsyncQueryAsync<T>(
		Expression expression,
		EsqlAsyncQueryOptions? asyncOptions,
		CancellationToken cancellationToken)
	{
		var pollInterval = ResolvePollInterval(asyncOptions);
		var (esql, query) = TranslateAndFormat(expression);
		var options = query.Parameters is { } p
			? (asyncOptions ?? new EsqlAsyncQueryOptions()) with { Parameters = p.ToEsqlParams() }
			: asyncOptions;
		var response = await _executor
			.SubmitAsyncQueryAsync(esql, options, cancellationToken)
			.ConfigureAwait(false);
		var result = await _reader
			.ReadRowsWithMetadataAsync<T>(response.Body, cancellationToken)
			.ConfigureAwait(false);
		return new EsqlAsyncQuery<T>(_executor, result, response, _reader, pollInterval);
	}

	private TResult ExecuteCore<TResult>(Expression expression)
	{
		var (esql, query) = TranslateAndFormat(expression);

		if (GetElementType(expression) is not null)
		{
			throw new NotSupportedException(
				$"Collection queries must use {nameof(ExecuteEnumerable)} via GetEnumerator(). " +
				$"Execute<{typeof(TResult).Name}> is only supported for scalar results.");
		}

		return ExecuteScalar<TResult>(esql, query, expression);
	}

	private TResult ExecuteScalar<TResult>(string esql, EsqlQuery query, Expression expression)
	{
		var options = query.Parameters is { } p ? new EsqlQueryOptions { Parameters = p.ToEsqlParams() } : null;
		using var response = _executor.ExecuteQuery(esql, options);
		var result = _reader.ReadScalar<TResult>(response.Body);

		ValidateScalarCardinality(expression, result.RowCount);
		return result.Value!;
	}

	/// <summary>Executes a collection query and returns rows as the correct element type.</summary>
	internal IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
	{
		var (esql, query) = TranslateAndFormat(expression);
		var options = query.Parameters is { } p ? new EsqlQueryOptions { Parameters = p.ToEsqlParams() } : null;
		using var response = _executor.ExecuteQuery(esql, options);

		foreach (var item in _reader.ReadRows<TElement>(response.Body))
			yield return item;
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

			case nameof(Queryable.Any):
			case nameof(EsqlQueryableExtensions.AnyAsync):
				if (rowCount != 1)
					throw new InvalidOperationException($"Operation '{methodName}' expected exactly one row but got {rowCount}");
				break;

			case nameof(Queryable.Count):
			case nameof(EsqlQueryableExtensions.CountAsync):
			case nameof(Queryable.LongCount):
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

	private static TimeSpan ResolvePollInterval(EsqlAsyncQueryOptions? asyncOptions)
	{
		if (asyncOptions?.PollInterval is not { } pollInterval)
			return TimeSpan.FromMilliseconds(100);

		if (pollInterval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(asyncOptions), "The PollInterval value must be greater than zero.");

		return pollInterval;
	}

	private static Type? GetElementType(Expression expression)
	{
		Verify.NotNull(expression);

		var type = TypeHelper.FindGenericType(typeof(IQueryable<>), expression.Type);
		if (type is null)
			return null;

		return type.GetGenericArguments()[0];
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DefaultJsonTypeInfoResolver is a fallback; the user-provided JsonSerializerContext is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DefaultJsonTypeInfoResolver is a fallback; the user-provided JsonSerializerContext is expected to include an AOT-safe TypeInfoResolver.")]
	private static JsonSerializerOptions ResolveOptions(JsonSerializerContext context) =>
		new()
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(context, new DefaultJsonTypeInfoResolver()),
			PropertyNamingPolicy = context.Options.PropertyNamingPolicy
		};

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Default options are a convenience fallback; Native AOT scenarios should pass explicit JsonSerializerOptions/JsonSerializerContext.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Default options are a convenience fallback; trimming-safe scenarios should pass explicit JsonSerializerOptions/JsonSerializerContext.")]
	private static JsonSerializerOptions CreateDefaultJsonOptions() =>
		new(JsonSerializerOptions.Default)
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
}
