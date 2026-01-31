// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Linq.Expressions;
using Elastic.Esql.Generation;

namespace Elastic.Esql.Core;

/// <summary>
/// IQueryable implementation for ES|QL queries.
/// </summary>
public class EsqlQueryable<T> : IEsqlQueryable<T>, IOrderedQueryable<T>
{
	private readonly EsqlQueryProvider _provider;

	/// <summary>
	/// Creates a new queryable from a constant (root query).
	/// </summary>
	public EsqlQueryable(EsqlQueryProvider provider)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		Expression = Expression.Constant(this);
	}

	/// <summary>
	/// Creates a new queryable from an expression.
	/// </summary>
	public EsqlQueryable(EsqlQueryProvider provider, Expression expression)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		Expression = expression ?? throw new ArgumentNullException(nameof(expression));
	}

	/// <inheritdoc/>
	public Type ElementType => typeof(T);

	/// <inheritdoc/>
	public Expression Expression { get; }

	/// <inheritdoc/>
	public IQueryProvider Provider => _provider;

	/// <inheritdoc/>
	public EsqlQueryContext Context => _provider.Context;

	/// <inheritdoc/>
	public string ToEsqlString()
	{
		var query = _provider.TranslateExpression(Expression);
		var generator = new EsqlGenerator();
		return generator.Generate(query);
	}

	/// <summary>
	/// Returns the ES|QL query string representation.
	/// </summary>
	public override string ToString() => ToEsqlString();

	/// <inheritdoc/>
	public IEnumerator<T> GetEnumerator() => _provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <inheritdoc/>
	public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) => new AsyncEnumerableWrapper<T>(_provider.ExecuteStreamingAsync<T>(Expression, cancellationToken));

	/// <summary>
	/// Wrapper to expose async enumeration without implementing IAsyncEnumerable directly.
	/// </summary>
	private sealed class AsyncEnumerableWrapper<TItem>(IAsyncEnumerable<TItem> source)
		: IAsyncEnumerable<TItem>
	{
		public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
			=> source.GetAsyncEnumerator(cancellationToken);
	}
}
