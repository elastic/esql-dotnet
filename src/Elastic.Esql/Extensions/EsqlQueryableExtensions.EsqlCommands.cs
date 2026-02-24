// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	/// <summary>
	/// Produces a row with one or more columns with values that you specify (ROW command).
	/// </summary>
	public static IQueryable<TSource> Row<TSource>(this IQueryable<TSource> source, Expression<Func<object>> columns)
	{
		Verify.NotNull(source);
		Verify.NotNull(columns);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<object>>, IQueryable<TSource>>(Row).Method,
			Expression.Quote(columns)
		);
	}

	/// <summary>
	/// Specifies the index pattern to use for the query.
	/// </summary>
	public static IQueryable<TSource> From<TSource>(this IQueryable<TSource> source, string indexPattern)
	{
		Verify.NotNull(source);
		Verify.NotNull(indexPattern);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string, IQueryable<TSource>>(From).Method,
			Expression.Constant(indexPattern)
		);
	}

	/// <summary>
	/// Specifies fields to keep in the result (KEEP command).
	/// </summary>
	public static IQueryable<TSource> Keep<TSource>(this IQueryable<TSource> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string[], IQueryable<TSource>>(Keep).Method,
			Expression.Constant(fields)
		);
	}

	/// <summary>
	/// Specifies fields to keep using lambda selectors (KEEP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<TSource> Keep<TSource>(this IQueryable<TSource> source, params Expression<Func<TSource, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, object?>>[], IQueryable<TSource>>(Keep).Method,
			Expression.NewArrayInit(typeof(Expression<Func<TSource, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}

	/// <summary>
	/// Specifies fields to keep with optional aliases via a projection.
	/// </summary>
	public static IQueryable<TSource> Keep<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> projection)
	{
		Verify.NotNull(source);
		Verify.NotNull(projection);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, TResult>>, IQueryable<TSource>>(Keep).Method,
			Expression.Quote(projection)
		);
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	public static IQueryable<TSource> Drop<TSource>(this IQueryable<TSource> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string[], IQueryable<TSource>>(Drop).Method,
			Expression.Constant(fields)
		);
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<TSource> Drop<TSource>(this IQueryable<TSource> source, params Expression<Func<TSource, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, object?>>[], IQueryable<TSource>>(Drop).Method,
			Expression.NewArrayInit(typeof(Expression<Func<TSource, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}

	/// <summary>
	/// Adds a COMPLETION command using a string field name as the prompt.
	/// </summary>
	public static IQueryable<TSource> Completion<TSource>(this IQueryable<TSource> source, string prompt, string inferenceId, string? column = null)
	{
		Verify.NotNull(source);
		Verify.NotNull(prompt);
		Verify.NotNull(inferenceId);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, string, string, string?, IQueryable<TSource>>(Completion).Method,
			Expression.Constant(prompt),
			Expression.Constant(inferenceId),
			Expression.Constant(column, typeof(string))
		);
	}

	/// <summary>
	/// Adds a COMPLETION command using a lambda selector as the prompt field.
	/// </summary>
	public static IQueryable<TSource> Completion<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, string>> promptSelector, string inferenceId, string? column = null)
	{
		Verify.NotNull(source);
		Verify.NotNull(promptSelector);
		Verify.NotNull(inferenceId);

		return CreateQuery(source,
			new Func<IQueryable<TSource>, Expression<Func<TSource, string>>, string, string?, IQueryable<TSource>>(Completion).Method,
			Expression.Quote(promptSelector),
			Expression.Constant(inferenceId),
			Expression.Constant(column, typeof(string))
		);
	}

	/// <summary>
	/// Adds a full-text match filter.
	/// </summary>
	public static IQueryable<T> Match<T>(this IQueryable<T> source, string field, string query) =>
		// This will be handled by the `WhereClauseVisitor` when it sees `EsqlFunctions.Match`
		source.Where(_ => Functions.EsqlFunctions.Match(field, query));
}
