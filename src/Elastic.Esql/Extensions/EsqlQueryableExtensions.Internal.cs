// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Elastic.Esql.Core;

namespace Elastic.Esql.Extensions;

public static partial class EsqlQueryableExtensions
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static IQueryable<TSource> CreateQuery<TSource>(IQueryable<TSource> source, MethodInfo method, params ReadOnlySpan<Expression> arguments) =>
		source.Provider.CreateQuery<TSource>(
			Expression.Call(
				instance: null,
				method: method,
				arguments:
				[
					source.Expression,
					.. arguments
				]));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static IQueryable<TResult> CreateQuery<TSource, TResult>(IQueryable<TSource> source, MethodInfo method, params ReadOnlySpan<Expression> arguments) =>
		source.Provider.CreateQuery<TResult>(
			Expression.Call(
				instance: null,
				method: method,
				arguments:
				[
					source.Expression,
					.. arguments
				]));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static TResult Execute<TSource, TResult>(IQueryable<TSource> source, MethodInfo method, params ReadOnlySpan<Expression> arguments) =>
		source.Provider.Execute<TResult>(
			Expression.Call(
				instance: null,
				method: method,
				arguments:
				[
					source.Expression,
					.. arguments
				]));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Task<TResult> ExecuteAsync<TSource, TResult>(IQueryable<TSource> source, MethodInfo method, CancellationToken cancellationToken,
		params ReadOnlySpan<Expression> arguments)
	{
		if (source.Provider is not EsqlQueryProvider provider)
			throw new NotSupportedException($"This method is only valid for '{nameof(EsqlQueryable<>)}'.");

		return provider.ExecuteAsync<TResult>(
			Expression.Call(
				instance: null,
				method: method,
				arguments:
				[
					source.Expression,
					.. arguments
				]),
			cancellationToken);
	}
}
