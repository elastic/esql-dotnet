// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;
using Elastic.Esql.FieldMetadataResolver;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Core;

/// <summary>
/// IQueryable implementation for ES|QL queries.
/// </summary>
public sealed class EsqlQueryable<T> : IEsqlQueryable<T>, IOrderedQueryable<T>
{
	/// <inheritdoc/>
	public Type ElementType => typeof(T);

	/// <inheritdoc/>
	public Expression Expression { get; }

	/// <inheritdoc cref="IQueryable.Provider"/>
	public EsqlQueryProvider Provider { get; }

	/// <inheritdoc/>
	IQueryProvider IQueryable.Provider => Provider;

	/// <summary>
	/// Creates a new ESQL queryable.
	/// </summary>
	/// <remarks>
	///	The resulting queryable will use the reflection based <see cref="SystemTextJsonFieldMetadataResolver"/> to resolve field metadata.
	/// In AOT context, please use the <see cref="EsqlQueryable{T}(EsqlQueryProvider)"/> overload instead.
	/// <para>
	/// The <see cref="SystemTextJsonFieldMetadataResolver"/> is fully AOT compatible when initializing it using a <see cref="JsonSerializerOptions"/>
	/// instance that is linked to a source generated <see cref="JsonSerializerOptions.TypeInfoResolver"/> context.
	/// </para>
	/// <para>
	///	The <c>Elastic.Clients.Esql</c> and <c>Elastic.Clients.Elasticsearch</c> packages also provide AOT compatible <see cref="IEsqlFieldMetadataResolver"/>
	/// implementations utilizing the capabilities of the <c>Elastic.Mapping</c> framework.
	/// </para>
	/// </remarks>
	public EsqlQueryable()
	{
		Provider = new EsqlQueryProvider(new SystemTextJsonFieldMetadataResolver(null));
		Expression = Expression.Constant(this);
	}

	/// <summary>
	/// Creates a new ESQL queryable using the specified <paramref name="provider"/>.
	/// </summary>
	/// <param name="provider">The <see cref="EsqlQueryProvider"/> to use.</param>
	public EsqlQueryable(EsqlQueryProvider provider)
	{
		Verify.NotNull(provider);

		Provider = provider;
		Expression = Expression.Constant(this);
	}

	/// <summary>
	/// Creates a new queryable from an expression.
	/// </summary>
	public EsqlQueryable(EsqlQueryProvider provider, Expression expression)
	{
		Verify.NotNull(provider);
		Verify.NotNull(expression);

		if (!typeof(IQueryable<T>).IsAssignableFrom(expression.Type))
		{
			throw new ArgumentException("Expression is not assignable to 'IQueryable<T>'.", nameof(expression));
		}

		Provider = provider;
		Expression = expression;
	}

	/// <inheritdoc/>
	public string ToEsqlString(bool inlineParameters)
	{
		var query = Provider.TranslateExpression(Expression, inlineParameters);
		var formatter = new EsqlFormatter();

		return formatter.Format(query);
	}

	/// <inheritdoc/>
	public EsqlParameters? GetParameters()
	{
		var query = Provider.TranslateExpression(Expression, false);

		return query.Parameters;
	}

	/// <summary>
	/// Returns the ES|QL query string representation.
	/// </summary>
	public override string ToString() => ToEsqlString(true);

	/// <inheritdoc/>
	public IEnumerator<T> GetEnumerator() => Provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <inheritdoc/>
	public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
		new AsyncEnumerableWrapper<T>(Provider.ExecuteStreamingAsync<T>(Expression, cancellationToken));

	/// <summary>
	/// Wrapper to expose async enumeration without implementing <see cref="IAsyncEnumerable{T}"/> directly.
	/// </summary>
	private sealed class AsyncEnumerableWrapper<TItem>(IAsyncEnumerable<TItem> source) : IAsyncEnumerable<TItem>
	{
		public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
			=> source.GetAsyncEnumerator(cancellationToken);
	}
}
