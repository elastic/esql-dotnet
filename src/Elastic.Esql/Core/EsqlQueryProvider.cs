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
/// The base ES|QL query provider.
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

		var elementType = GetElementType(expression)
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

		return ExecuteCore<object?>(expression);
	}

	/// <inheritdoc/>
	public TResult Execute<TResult>(Expression expression)
	{
		Verify.NotNull(expression);

		return ExecuteCore<TResult>(expression);
	}

	/// <summary>
	/// Asynchronously executes the query represented by a specified expression tree.
	/// </summary>
	/// <param name="expression">An expression tree that represents a LINQ query.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The value that results from executing the specified query.</returns>
	public Task<object?> ExecuteAsync(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		return ExecuteCoreAsync<object?>(expression, cancellationToken);
	}

	/// <summary>
	/// Asynchronously executes the strongly-typed query represented by a specified expression tree.
	/// </summary>
	/// <typeparam name="TResult">The type of the value that results from executing the query.</typeparam>
	/// <param name="expression">An expression tree that represents a LINQ query.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The value that results from executing the specified query.</returns>
	public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		return ExecuteCoreAsync<TResult>(expression, cancellationToken);
	}

	/// <summary>
	/// Executes the specified query expression asynchronously and returns the results as a stream of elements.
	/// </summary>
	/// <typeparam name="TElement">The type of the elements returned by the query.</typeparam>
	/// <param name="expression">An expression representing the query to execute.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>An asynchronous stream of elements of type <typeparamref name="TElement"/> resulting from the execution of the query.</returns>
	public IAsyncEnumerable<TElement> ExecuteStreamingAsync<TElement>(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		var elementType = GetElementType(expression);
		if (elementType is null || elementType != typeof(TElement))
			throw new ArgumentException($"Expression must return a queryable of '{typeof(TElement).Name}' elements.", nameof(expression));

		return ExecuteCoreStreamingAsync<TElement>(expression, cancellationToken);
	}

	/// <summary>
	/// Executes the query represented by a specified expression tree.
	/// </summary>
	/// <typeparam name="TResult">The type of the value that results from executing the query.</typeparam>
	/// <param name="expression">An expression tree that represents a LINQ query.</param>
	/// <returns>The value that results from executing the specified query.</returns>
	protected virtual TResult ExecuteCore<TResult>(Expression expression)
	{
		Verify.NotNull(expression);

		throw new InvalidOperationException($"This '{nameof(EsqlQueryProvider)}' implementation does not support query execution.");
	}

	/// <summary>
	/// Asynchronously executes the query represented by a specified expression tree.
	/// </summary>
	/// <typeparam name="TResult">The type of the value that results from executing the query.</typeparam>
	/// <param name="expression">An expression tree that represents a LINQ query.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The value that results from executing the specified query.</returns>
	protected virtual Task<TResult> ExecuteCoreAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		throw new InvalidOperationException($"This '{nameof(EsqlQueryProvider)}' implementation does not support asynchronous query execution.");
	}

	/// <summary>
	/// Executes the specified query expression asynchronously and returns the results as a stream of elements.
	/// </summary>
	/// <typeparam name="TElement">The type of the elements returned by the query.</typeparam>
	/// <param name="expression">An expression representing the query to execute.</param>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
	/// <returns>An asynchronous stream of elements of type <typeparamref name="TElement"/> resulting from the execution of the query.</returns>
	protected virtual IAsyncEnumerable<TElement> ExecuteCoreStreamingAsync<TElement>(Expression expression, CancellationToken cancellationToken)
	{
		Verify.NotNull(expression);

		throw new InvalidOperationException($"This '{nameof(EsqlQueryProvider)}' implementation does not support streaming query execution.");
	}

	/// <summary>
	/// Translates the specified LINQ expression into an equivalent ES|QL query representation.
	/// </summary>
	/// <param name="expression">The LINQ expression to translate.</param>
	/// <param name="inlineParameters">Set <see langword="true"/> to inline captured variables instead of translating them to <c>?name</c> placeholders.</param>
	/// <returns>An <see cref="EsqlQuery"/> object representing the translated ES|QL query.</returns>
	protected internal EsqlQuery TranslateExpression(Expression expression, bool inlineParameters)
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
	/// Determines the element-type of a queryable expression.
	/// </summary>
	/// <param name="expression">The queryable expression to determine the element-type for.</param>
	/// <returns>The element-type if the specified type is a supported queryable type or <see langword="null"/>, if not.</returns>
	protected static Type? GetElementType(Expression expression)
	{
		Verify.NotNull(expression);

		var type = FindGenericType(typeof(IQueryable<>), expression.Type);
		if (type is null)
			return null;

		return type.GetGenericArguments()[0];
	}

	/// <summary>
	/// Searches the inheritance hierarchy of a given type to locate a constructed generic type that matches the specified generic type definition.
	/// </summary>
	/// <param name="definition">The generic type definition to search for. Must be an open generic type (for example, <c>typeof(IQueryable&lt;&gt;)</c>).</param>
	/// <param name="type">The type whose inheritance hierarchy is searched for a matching constructed generic type.</param>
	/// <returns>
	/// A <see cref="Type"/> object representing the constructed generic type that matches the specified definition, or <see langword="null"/>> if no such
	/// type is found in the inheritance hierarchy.
	/// </returns>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070:RequiresUnreferencedCode",
		Justification = "GetInterfaces is only called if 'definition' is interface type. " +
						"In that case though the interface must be present (otherwise the Type of it could not exist) " +
						"which also means that the trimmer kept the interface and thus kept it on all types " +
						"which implement it. It doesn't matter if the GetInterfaces call below returns fewer types" +
						"as long as it returns the 'definition' as well.")]
	private static Type? FindGenericType(Type definition, Type? type)
	{
		bool? definitionIsInterface = null;

		while (type is not null && type != typeof(object))
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
				return type;

			definitionIsInterface ??= definition.IsInterface;

			if (definitionIsInterface.GetValueOrDefault())
			{
				foreach (var itype in type.GetInterfaces())
				{
					var found = FindGenericType(definition, itype);
					if (found is not null)
						return found;
				}
			}

			type = type.BaseType!;
		}

		return null;
	}
}
