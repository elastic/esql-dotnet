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
	private static readonly MethodInfo MethodFrom = GetMethodInfo(static (IQueryable<object> q) => q.From(null!));
	private static readonly MethodInfo MethodKeepString = GetMethodInfo(static (IQueryable<object> q) => q.Keep((string[])null!));
	private static readonly MethodInfo MethodKeepSelector = GetMethodInfo(static (IQueryable<object> q) => q.Keep((Expression<Func<object, object?>>[])null!));
	private static readonly MethodInfo MethodKeepProjection = GetMethodInfo(static (IQueryable<object> q) => q.Keep((Expression<Func<object, object>>)null!));
	private static readonly MethodInfo MethodDropString = GetMethodInfo(static (IQueryable<object> q) => q.Drop((string[])null!));
	private static readonly MethodInfo MethodDropSelector = GetMethodInfo(static (IQueryable<object> q) => q.Drop((Expression<Func<object, object?>>[])null!));
	private static readonly MethodInfo MethodRow = GetMethodInfo(static (IQueryable<object> q) => q.Row(null!));
	private static readonly MethodInfo MethodCompletionString = GetMethodInfo(static (IQueryable<object> q) => q.Completion((string)null!, null!, null));
	private static readonly MethodInfo MethodCompletionSelector = GetMethodInfo(static (IQueryable<object> q) => q.Completion((Expression<Func<object, string>>)null!, null!, null));

	/// <summary>
	/// Produces a row with one or more columns with values that you specify (ROW command).
	/// </summary>
	public static IQueryable<T> Row<T>(this IQueryable<T> source, Expression<Func<object>> columns)
	{
		Verify.NotNull(source);
		Verify.NotNull(columns);

		return ApplyMethod(source, MethodRow, Expression.Quote(columns));
	}

	/// <summary>
	/// Specifies the index pattern to use for the query.
	/// </summary>
	public static IQueryable<T> From<T>(this IQueryable<T> source, string indexPattern)
	{
		Verify.NotNull(source);
		Verify.NotNull(indexPattern);

		return ApplyMethod(source, MethodFrom, Expression.Constant(indexPattern));
	}

	/// <summary>
	/// Specifies fields to keep in the result (KEEP command).
	/// </summary>
	public static IQueryable<T> Keep<T>(this IQueryable<T> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return ApplyMethod(source, MethodKeepString, Expression.Constant(fields));
	}

	/// <summary>
	/// Specifies fields to keep using lambda selectors (KEEP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<T> Keep<T>(this IQueryable<T> source, params Expression<Func<T, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return ApplyMethod(source, MethodKeepSelector,
			Expression.NewArrayInit(typeof(Expression<Func<T, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}

	/// <summary>
	/// Specifies fields to keep with optional aliases via a projection.
	/// </summary>
	public static IQueryable<T> Keep<T, TResult>(this IQueryable<T> source, Expression<Func<T, TResult>> projection)
	{
		Verify.NotNull(source);
		Verify.NotNull(projection);

		return ApplyMethod<T, TResult>(source, MethodKeepProjection, Expression.Quote(projection));
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	public static IQueryable<T> Drop<T>(this IQueryable<T> source, params string[] fields)
	{
		Verify.NotNull(source);
		Verify.NotNull(fields);

		return ApplyMethod(source, MethodDropString, Expression.Constant(fields));
	}

	/// <summary>
	/// Specifies fields to drop from the result (DROP command).
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Target array type is statically referenced in the expression tree.")]
	public static IQueryable<T> Drop<T>(this IQueryable<T> source, params Expression<Func<T, object?>>[] fieldSelectors)
	{
		Verify.NotNull(source);
		Verify.NotNull(fieldSelectors);

		return ApplyMethod(source, MethodDropSelector,
			Expression.NewArrayInit(typeof(Expression<Func<T, object?>>), fieldSelectors.Select(Expression.Quote))
		);
	}

	/// <summary>
	/// Adds a COMPLETION command using a string field name as the prompt.
	/// </summary>
	public static IQueryable<T> Completion<T>(this IQueryable<T> source, string prompt, string inferenceId, string? column = null)
	{
		Verify.NotNull(source);
		Verify.NotNull(prompt);
		Verify.NotNull(inferenceId);

		return ApplyMethod(source, MethodCompletionString,
			Expression.Constant(prompt),
			Expression.Constant(inferenceId),
			Expression.Constant(column, typeof(string))
		);
	}

	/// <summary>
	/// Adds a COMPLETION command using a lambda selector as the prompt field.
	/// </summary>
	public static IQueryable<T> Completion<T>(this IQueryable<T> source, Expression<Func<T, string>> promptSelector, string inferenceId, string? column = null)
	{
		Verify.NotNull(source);
		Verify.NotNull(promptSelector);
		Verify.NotNull(inferenceId);

		return ApplyMethod(source, MethodCompletionSelector,
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

	// -----

	[UnconditionalSuppressMessage("AOT", "IL2060", Justification = "Generic target method is statically referenced in the expression tree.")]
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic target method is statically referenced in the expression tree.")]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static IQueryable<TElement> ApplyMethod<TElement>(IQueryable<TElement> source, MethodInfo method, params ReadOnlySpan<Expression> arguments) =>
		source.Provider.CreateQuery<TElement>(
			Expression.Call(
				instance: null,
				method: method.MakeGenericMethod(typeof(TElement)),
				arguments:
				[
					source.Expression,
					.. arguments
				]));

	[UnconditionalSuppressMessage("AOT", "IL2060", Justification = "Generic target method is statically referenced in the expression tree.")]
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic target method is statically referenced in the expression tree.")]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static IQueryable<TElement> ApplyMethod<TElement, T1>(IQueryable<TElement> source, MethodInfo method, params ReadOnlySpan<Expression> arguments) =>
		source.Provider.CreateQuery<TElement>(
			Expression.Call(
				instance: null,
				method: method.MakeGenericMethod(typeof(TElement), typeof(T1)),
				arguments:
				[
					source.Expression,
					.. arguments
				]));

	/// <summary>
	/// Extracts the <see cref="MethodInfo"/> from a lambda that calls the target method.
	/// </summary>
	/// <remarks>
	///	The lambda is never executed. It's only used for compile-time overload resolution.
	/// </remarks>
	/// <param name="expression">The lambda expression that calls the target method.</param>
	/// <returns>The <see cref="MethodInfo"/> for the target method called in the lambda expression.</returns>
	private static MethodInfo GetMethodInfo(LambdaExpression expression) => ((MethodCallExpression)expression.Body).Method.GetGenericMethodDefinition();
}
