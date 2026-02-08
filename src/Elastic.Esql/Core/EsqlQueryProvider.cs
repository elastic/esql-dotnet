// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections;
using System.Linq.Expressions;
using Elastic.Esql.Execution;
using Elastic.Esql.Generation;
using Elastic.Esql.QueryModel;
using Elastic.Esql.Translation;

namespace Elastic.Esql.Core;

/// <summary>
/// Query provider that translates LINQ expressions to ES|QL.
/// </summary>
/// <remarks>
/// Creates a new query provider.
/// </remarks>
public class EsqlQueryProvider(EsqlQueryContext context) : IQueryProvider
{

	/// <summary>
	/// Gets the query context.
	/// </summary>
	public EsqlQueryContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

	/// <inheritdoc/>
	public IQueryable CreateQuery(Expression expression)
	{
		var elementType = GetElementType(expression.Type);
		var queryableType = typeof(EsqlQueryable<>).MakeGenericType(elementType);
		return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
	}

	/// <inheritdoc/>
	public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new EsqlQueryable<TElement>(this, expression);

	/// <inheritdoc/>
	public object? Execute(Expression expression)
	{
		var elementType = GetElementType(expression.Type);
		var executeMethod = typeof(EsqlQueryProvider)
			.GetMethod(nameof(Execute), 1, [typeof(Expression)])!
			.MakeGenericMethod(elementType);
		return executeMethod.Invoke(this, [expression]);
	}

	/// <inheritdoc/>
	public TResult Execute<TResult>(Expression expression) => ExecuteAsync<TResult>(expression, CancellationToken.None).GetAwaiter().GetResult();

	/// <summary>
	/// Executes the query asynchronously.
	/// </summary>
	public async Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
	{
		var executor = Context.Executor ?? throw new InvalidOperationException("No query executor configured. Provide an IEsqlQueryExecutor to execute queries.");

		var parameters = new EsqlParameters();
		Context.ParameterCollection = parameters;
		var esqlQuery = TranslateExpression(expression);
		var esqlString = GenerateEsqlString(esqlQuery);
		Context.ParameterCollection = null;

		var paramList = parameters.HasParameters ? parameters.ToEsqlParams() : null;

		// Determine if we're getting a single result or a collection
		var resultType = typeof(TResult);

		if (IsScalarResult(expression))
		{
			// Scalar aggregation (Count, Sum, etc.)
			var response = await executor.ExecuteAsync(esqlString, paramList, cancellationToken);
			return MaterializeScalar<TResult>(response);
		}

		if (IsSingleResult(expression))
		{
			// First, FirstOrDefault, Single, SingleOrDefault
			var response = await executor.ExecuteAsync(esqlString, paramList, cancellationToken);
			return MaterializeSingle<TResult>(response, esqlQuery, expression);
		}

		// Collection result
		var collectionResponse = await executor.ExecuteAsync(esqlString, paramList, cancellationToken);
		var elementType = GetElementTypeFromResult(resultType);
		var items = MaterializeCollection(collectionResponse, elementType, esqlQuery);

		// Convert to the expected result type
		if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(List<>))
		{
			var listType = typeof(List<>).MakeGenericType(elementType);
			var list = (IList)Activator.CreateInstance(listType)!;
			foreach (var item in items)
				_ = list.Add(item);
			return (TResult)list;
		}

		return (TResult)items;
	}

	/// <summary>
	/// Translates a LINQ expression to an ES|QL query model.
	/// </summary>
	public EsqlQuery TranslateExpression(Expression expression)
	{
		var visitor = new EsqlExpressionVisitor(Context);
		return visitor.Translate(expression);
	}

	/// <summary>
	/// Generates an ES|QL string from a query model.
	/// </summary>
	public string GenerateEsqlString(EsqlQuery query)
	{
		var generator = new EsqlGenerator();
		return generator.Generate(query);
	}

	/// <summary>
	/// Gets an async enumerable for streaming results.
	/// </summary>
	public async IAsyncEnumerable<T> ExecuteStreamingAsync<T>(
		Expression expression,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var executor = Context.Executor ?? throw new InvalidOperationException("No query executor configured. Provide an IEsqlQueryExecutor to execute queries.");

		var parameters = new EsqlParameters();
		Context.ParameterCollection = parameters;
		var esqlQuery = TranslateExpression(expression);
		var esqlString = GenerateEsqlString(esqlQuery);
		Context.ParameterCollection = null;

		var paramList = parameters.HasParameters ? parameters.ToEsqlParams() : null;
		var response = await executor.ExecuteAsync(esqlString, paramList, cancellationToken);
		var materializer = new ResultMaterializer(Context.MetadataResolver);

		foreach (var item in materializer.Materialize<T>(response, esqlQuery))
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return item;
		}
	}

	private static Type GetElementType(Type type)
	{
		if (type.IsGenericType)
		{
			var genericDef = type.GetGenericTypeDefinition();
			if (genericDef == typeof(IQueryable<>) ||
				genericDef == typeof(IEnumerable<>) ||
				genericDef == typeof(IAsyncEnumerable<>))
				return type.GetGenericArguments()[0];
		}

		foreach (var iface in type.GetInterfaces())
		{
			if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				return iface.GetGenericArguments()[0];
		}

		return type;
	}

	private static Type GetElementTypeFromResult(Type resultType)
	{
		if (resultType.IsGenericType)
		{
			var genericDef = resultType.GetGenericTypeDefinition();
			if (genericDef == typeof(List<>) ||
				genericDef == typeof(IEnumerable<>) ||
				genericDef == typeof(IList<>) ||
				genericDef == typeof(ICollection<>))
				return resultType.GetGenericArguments()[0];
		}

		return resultType;
	}

	private static bool IsScalarResult(Expression expression)
	{
		if (expression is MethodCallExpression methodCall)
		{
			var methodName = methodCall.Method.Name;
			return methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max" or "Any" or "All";
		}
		return false;
	}

	private static bool IsSingleResult(Expression expression)
	{
		if (expression is MethodCallExpression methodCall)
		{
			var methodName = methodCall.Method.Name;
			return methodName is "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Last" or "LastOrDefault";
		}
		return false;
	}

	private TResult MaterializeScalar<TResult>(EsqlResponse response)
	{
		var materializer = new ResultMaterializer(Context.MetadataResolver);
		return materializer.MaterializeScalar<TResult>(response);
	}

	private TResult MaterializeSingle<TResult>(EsqlResponse response, EsqlQuery query, Expression expression)
	{
		var materializer = new ResultMaterializer(Context.MetadataResolver);
		var items = materializer.Materialize<TResult>(response, query).ToList();

		var methodName = (expression as MethodCallExpression)?.Method.Name ?? "";

		return methodName switch
		{
			"First" => items.First(),
			"FirstOrDefault" => items.FirstOrDefault()!,
			"Single" => items.Single(),
			"SingleOrDefault" => items.SingleOrDefault()!,
			"Last" => items.Last(),
			"LastOrDefault" => items.LastOrDefault()!,
			_ => items.FirstOrDefault()!
		};
	}

	private System.Collections.IEnumerable MaterializeCollection(EsqlResponse response, Type elementType, EsqlQuery query)
	{
		var materializer = new ResultMaterializer(Context.MetadataResolver);
		var materializeMethod = typeof(ResultMaterializer)
			.GetMethod(nameof(ResultMaterializer.Materialize))!
			.MakeGenericMethod(elementType);
		return (System.Collections.IEnumerable)materializeMethod.Invoke(materializer, [response, query])!;
	}
}
