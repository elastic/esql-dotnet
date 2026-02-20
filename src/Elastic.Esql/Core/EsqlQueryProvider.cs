// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Elastic.Esql.FieldMetadataResolver;
using Elastic.Esql.QueryModel;
using Elastic.Esql.Translation;
using Elastic.Esql.Validation;

namespace Elastic.Esql.Core;

/// <summary>
/// Query provider that translates LINQ expressions to ES|QL.
/// </summary>
public class EsqlQueryProvider(IEsqlFieldMetadataResolver fieldMetadataResolver) : IQueryProvider
{
	/// <summary>
	/// The resolver for field metadata resolution.
	/// </summary>
	public IEsqlFieldMetadataResolver FieldMetadataResolver { get; } = fieldMetadataResolver;

	/// <inheritdoc/>
	public IQueryable CreateQuery(Expression expression)
	{
		Verify.NotNull(expression);

		var elementType = GetElementType(expression.Type)
						  ?? throw new ArgumentException("Expression does not represent a queryable sequence.", nameof(expression));

		var queryableType = typeof(EsqlQueryable<>).MakeGenericType(elementType);

		try
		{
			return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
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

		var elementType = GetElementType(expression.Type)
						  ?? throw new ArgumentException("Expression does not represent a queryable sequence.", nameof(expression));

		var executeMethod = typeof(EsqlQueryProvider)
			.GetMethods()
			.Single(m => m is { Name: nameof(Execute), IsGenericMethodDefinition: true })
			.MakeGenericMethod(elementType);
		return executeMethod.Invoke(this, [expression]);
	}

	/// <inheritdoc/>
	public virtual TResult Execute<TResult>(Expression expression)
	{
		Verify.NotNull(expression);

		return ExecuteAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();
	}

	/// <summary>Asynchronously executes the strongly-typed query represented by a specified expression tree.</summary>
	/// <param name="expression">An expression tree that represents a LINQ query.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <typeparam name="TResult">The type of the value that results from executing the query.</typeparam>
	/// <returns>The value that results from executing the specified query.</returns>
	public virtual Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken) =>
		throw new InvalidOperationException($"This '{nameof(EsqlQueryProvider)}' implementation does not support query execution.");

	/// <summary>
	/// Executes the specified query expression asynchronously and returns the results as a stream of elements.
	/// </summary>
	/// <typeparam name="T">The type of the elements returned by the query.</typeparam>
	/// <param name="expression">An expression representing the query to execute.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>An asynchronous stream of elements of type <typeparamref name="T"/> resulting from the execution of the query.</returns>
	public virtual IAsyncEnumerable<T> ExecuteStreamingAsync<T>(Expression expression, CancellationToken cancellationToken) =>
		throw new InvalidOperationException($"This '{nameof(EsqlQueryProvider)}' implementation does not support query execution.");

	/// <summary>
	/// Translates the specified LINQ expression into an equivalent ESQL query representation.
	/// </summary>
	/// <param name="expression">The LINQ expression to translate.</param>
	/// <param name="inlineParameters">Set <see langword="true"/> to inline captured variables instead of translating them to <c>?name</c> placeholders.</param>
	/// <returns>An <see cref="EsqlQuery"/> object representing the translated ESQL query.</returns>
	public EsqlQuery TranslateExpression(Expression expression, bool inlineParameters)
	{
#if NET8_0_OR_GREATER
		ArgumentNullException.ThrowIfNull(expression);
#else
		if (expression is null)
			throw new ArgumentNullException(nameof(expression));
#endif

		var visitor = new EsqlExpressionVisitor(this, null /* TODO: Implement */, inlineParameters);

		return visitor.Translate(expression);
	}

	/// <summary>
	/// Determines the element-type of a queryable type.
	/// </summary>
	/// <param name="type">The queryable type to determine the element-type for.</param>
	/// <returns>The element-type if the specified type is a supported queryable type or <see langword="null"/>, if not.</returns>
	protected static Type? GetElementType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
	{
		Verify.NotNull(type);

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
		{
			return type.GetGenericArguments()[0];
		}

		foreach (var iface in type.GetInterfaces())
		{
			if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IQueryable<>))
				return iface.GetGenericArguments()[0];
		}

		return null;
	}

	/// <summary>
	/// Determines whether the specified expression represents a scalar LINQ result operation.
	/// </summary>
	/// <param name="expression">The expression to evaluate.</param>
	/// <returns><see langword="true"/> if the expression is a method call corresponding to a scalar LINQ result operation or <see langword="false"/>, if not.</returns>
	protected static bool IsScalarResult(Expression expression)
	{
		Verify.NotNull(expression);

		return expression is MethodCallExpression
		{
			Method.Name:
			nameof(Enumerable.Count) or
			nameof(Enumerable.LongCount) or
			nameof(Enumerable.Sum) or
			nameof(Enumerable.Average) or
			nameof(Enumerable.Min) or
			nameof(Enumerable.Max) or
			nameof(Enumerable.Any) or
			nameof(Enumerable.All)
		};
	}

	/// <summary>
	/// Determines whether the specified expression represents a method call that returns a single result from a sequence.
	/// </summary>
	/// <param name="expression">The expression to evaluate.</param>
	/// <returns><see langword="true"/> if the expression represents a method call that returns a single result or <see langword="false"/>, if not.</returns>
	protected static bool IsSingleResult(Expression expression)
	{
		Verify.NotNull(expression);

		return expression is MethodCallExpression
		{
			Method.Name:
			nameof(Enumerable.First) or
			nameof(Enumerable.FirstOrDefault) or
			nameof(Enumerable.Single) or
			nameof(Enumerable.SingleOrDefault) or
			nameof(Enumerable.Last) or
			nameof(Enumerable.LastOrDefault)
		};
	}
}
