// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Elastic.Esql.Core;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;
using Elastic.Esql.Translation;

namespace Elastic.Esql.Extensions;

/// <summary>
/// Extension methods for ES|QL queryables.
/// </summary>
public static class EsqlQueryableExtensions
{
	/// <summary>
	/// Executes the query and returns results as a list.
	/// </summary>
	public static async Task<List<T>> ToListAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var list = new List<T>();
		await foreach (var item in queryable.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			list.Add(item);
		return list;
	}

	/// <summary>
	/// Executes the query and returns results as an array.
	/// </summary>
	public static async Task<T[]> ToArrayAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default) =>
		(await queryable.ToListAsync(cancellationToken)).ToArray();

	/// <summary>
	/// Returns the first element or throws if none exist.
	/// </summary>
	public static async Task<T> FirstAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return item;
		throw new InvalidOperationException("Sequence contains no elements.");
	}

	/// <summary>
	/// Returns the first element or default if none exist.
	/// </summary>
	public static async Task<T?> FirstOrDefaultAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return item;
		return default;
	}

	/// <summary>
	/// Returns the single element or throws.
	/// </summary>
	public static async Task<T> SingleAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		T? result = default;
		var found = false;

		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 2);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
		{
			if (found)
				throw new InvalidOperationException("Sequence contains more than one element.");
			result = item;
			found = true;
		}

		if (!found)
			throw new InvalidOperationException("Sequence contains no elements.");

		return result!;
	}

	/// <summary>
	/// Returns the single element or default.
	/// </summary>
	public static async Task<T?> SingleOrDefaultAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		T? result = default;
		var found = false;

		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 2);
		await foreach (var item in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
		{
			if (found)
				throw new InvalidOperationException("Sequence contains more than one element.");
			result = item;
			found = true;
		}

		return result;
	}

	/// <summary>
	/// Returns the count of elements.
	/// </summary>
	public static async Task<int> CountAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var executor = queryable.Context.Executor ?? throw new InvalidOperationException("No query executor configured. Provide an IEsqlQueryExecutor to execute queries.");

		// For count, we need to use STATS COUNT(*)
		var parameters = new EsqlParameters();
		queryable.Context.ParameterCollection = parameters;
		var esql = queryable.ToEsqlString();
		queryable.Context.ParameterCollection = null;

		// Append STATS COUNT(*) to the query
		var countQuery = esql + Environment.NewLine + "| STATS count = COUNT(*)";

		var paramList = parameters.HasParameters ? parameters.ToEsqlParams() : null;
		var response = await executor.ExecuteAsync(countQuery, paramList, cancellationToken);

		var materializer = new ResultMaterializer();
		return materializer.MaterializeScalar<int>(response);
	}

	/// <summary>
	/// Returns whether any elements exist.
	/// </summary>
	public static async Task<bool> AnyAsync<T>(
		this IEsqlQueryable<T> queryable,
		CancellationToken cancellationToken = default)
	{
		var limited = (IEsqlQueryable<T>)Queryable.Take(queryable, 1);
		await foreach (var _ in limited.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
			return true;
		return false;
	}

	/// <summary>
	/// Gets the ES|QL query string without executing.
	/// </summary>
	public static string ToEsqlString<T>(this IQueryable<T> queryable, bool inlineParameters = true)
	{
		if (queryable is IEsqlQueryable<T> esqlQueryable)
			return esqlQueryable.ToEsqlString(inlineParameters);

		throw new InvalidOperationException("Query is not an ES|QL query.");
	}

	/// <summary>
	/// Specifies fields to keep in the result (KEEP command).
	/// </summary>
	public static IQueryable<T> Keep<T>(
		this IQueryable<T> queryable,
		params string[] fields)
	{
		AsEsqlQueryable(queryable).Context.PendingCommands.Add(new KeepCommand(fields));
		return queryable;
	}

	/// <summary>
	/// Specifies fields to keep using lambda selectors (KEEP command). Fully AOT-safe.
	/// </summary>
	public static IQueryable<T> Keep<T>(
		this IQueryable<T> queryable,
		params Expression<Func<T, object?>>[] fieldSelectors)
	{
		var esql = AsEsqlQueryable(queryable);
		var fields = new string[fieldSelectors.Length];
		for (var i = 0; i < fieldSelectors.Length; i++)
		{
			var member = ExtractMember(fieldSelectors[i]);
			fields[i] = esql.Context.MetadataResolver.Resolve(member);
		}

		esql.Context.PendingCommands.Add(new KeepCommand(fields));
		return queryable;
	}

	/// <summary>
	/// Specifies fields to keep with optional aliases via a projection (KEEP/EVAL commands).
	/// Anonymous type projections require <c>[UnconditionalSuppressMessage]</c> at the call site under AOT.
	/// </summary>
#if NET8_0_OR_GREATER
	[UnconditionalSuppressMessage("Trimming", "IL2026",
		Justification = "Expression tree is only read for query translation, members are not invoked at runtime.")]
#endif
	public static IQueryable<T> Keep<T, TResult>(
		this IQueryable<T> queryable,
		Expression<Func<T, TResult>> projection)
	{
		var esql = AsEsqlQueryable(queryable);
		var projectionVisitor = new SelectProjectionVisitor(esql.Context);
		var result = projectionVisitor.Translate(projection);

		if (result.EvalExpressions.Count > 0)
			esql.Context.PendingCommands.Add(new EvalCommand(result.EvalExpressions));

		var keepFields = new List<string>(result.KeepFields);
		foreach (var eval in result.EvalExpressions)
		{
			var aliasName = eval.Substring(0, eval.IndexOf(" =", StringComparison.Ordinal));
			keepFields.Add(aliasName);
		}

		if (keepFields.Count > 0)
			esql.Context.PendingCommands.Add(new KeepCommand(keepFields));

		return queryable;
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	public static IQueryable<T> Drop<T>(
		this IQueryable<T> queryable,
		params string[] fields)
	{
		AsEsqlQueryable(queryable).Context.PendingCommands.Add(new DropCommand(fields));
		return queryable;
	}

	private static IEsqlQueryable<T> AsEsqlQueryable<T>(IQueryable<T> queryable) =>
		queryable as IEsqlQueryable<T>
		?? throw new InvalidOperationException("Query is not an ES|QL query.");

	private static MemberInfo ExtractMember<T>(Expression<Func<T, object?>> selector)
	{
		var body = selector.Body;

		// Unwrap Convert (boxing for value types)
		if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
			body = unary.Operand;

		if (body is MemberExpression member)
			return member.Member;

		throw new ArgumentException($"Expression must be a simple member access (e.g. x => x.Field), got: {selector.Body}");
	}

	/// <summary>
	/// Adds a full-text match filter.
	/// </summary>
	public static IQueryable<T> Match<T>(
		this IQueryable<T> queryable,
		string field,
		string query) =>
		// This will be handled by the WhereClauseVisitor when it sees EsqlFunctions.Match
		queryable.Where(_ => Elastic.Esql.Functions.EsqlFunctions.Match(field, query));

	/// <summary>Sets the timezone for this query.</summary>
	public static IEsqlQueryable<T> WithTimeZone<T>(this IEsqlQueryable<T> queryable, string timeZone)
	{
		queryable.Context.QueryOptions = (queryable.Context.QueryOptions ?? new EsqlQueryOptions()) with { TimeZone = timeZone };
		return queryable;
	}

	/// <summary>Sets the locale for this query.</summary>
	public static IEsqlQueryable<T> WithLocale<T>(this IEsqlQueryable<T> queryable, string locale)
	{
		queryable.Context.QueryOptions = (queryable.Context.QueryOptions ?? new EsqlQueryOptions()) with { Locale = locale };
		return queryable;
	}

	/// <summary>Sets parameters for this query (for ? placeholders in raw ES|QL).</summary>
	public static IEsqlQueryable<T> WithParameters<T>(this IEsqlQueryable<T> queryable, params object[] parameters)
	{
		queryable.Context.QueryOptions = (queryable.Context.QueryOptions ?? new EsqlQueryOptions()) with { Parameters = parameters };
		return queryable;
	}

	/// <summary>Includes profiling information in the response.</summary>
	public static IEsqlQueryable<T> WithProfile<T>(this IEsqlQueryable<T> queryable)
	{
		queryable.Context.QueryOptions = (queryable.Context.QueryOptions ?? new EsqlQueryOptions()) with { IncludeProfile = true };
		return queryable;
	}

	/// <summary>Sets columnar format for this query.</summary>
	public static IEsqlQueryable<T> WithColumnar<T>(this IEsqlQueryable<T> queryable, bool columnar = true)
	{
		queryable.Context.QueryOptions = (queryable.Context.QueryOptions ?? new EsqlQueryOptions()) with { Columnar = columnar };
		return queryable;
	}
}
