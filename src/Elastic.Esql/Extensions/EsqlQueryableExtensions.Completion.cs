// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Validation;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
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
}
