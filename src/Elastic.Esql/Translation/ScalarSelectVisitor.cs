// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elastic.Esql.Core;

namespace Elastic.Esql.Translation;

/// <summary>
/// Rewrites the operators that follow a Select returning a single value, such as
/// <c>Select(l => l.Duration * 3)</c> or <c>Select(l => l.Message)</c>, so that they read the column
/// the Select leaves: the field itself, or <c>result</c> for a computed value.
/// </summary>
internal sealed class ScalarSelectVisitor : ExpressionVisitor
{
	private static readonly HashSet<string> RowPreservingOperators =
	[
		nameof(Queryable.Where),
		nameof(Queryable.OrderBy),
		nameof(Queryable.OrderByDescending),
		nameof(Queryable.ThenBy),
		nameof(Queryable.ThenByDescending),
		nameof(Queryable.Take)
	];

	private static readonly HashSet<string> SelectorAggregates =
	[
		nameof(Queryable.Sum),
		nameof(Queryable.Average),
		nameof(Queryable.Min),
		nameof(Queryable.Max)
	];

	// The operators the translation supports over a row, whose lambdas take that row: after a single value they are
	// rewritten to read its column. Any other operator, such as Join, GroupJoin, SelectMany or TakeWhile, is left to
	// the translation, which refuses it with a message of its own.
	private static readonly HashSet<string> TranslatedOperators =
	[
		.. RowPreservingOperators,
		.. SelectorAggregates,
		nameof(Queryable.Select),
		nameof(Queryable.GroupBy),
		nameof(Queryable.First),
		nameof(Queryable.FirstOrDefault),
		nameof(Queryable.Single),
		nameof(Queryable.SingleOrDefault),
		nameof(Queryable.Count),
		nameof(Queryable.LongCount),
		nameof(Queryable.Any)
	];

	// Sum and Average have one overload per numeric type; Min and Max take the result type as a generic argument.
	private static readonly Dictionary<(string, Type), MethodInfo> NumericSelectorAggregates = new()
	{
		[(nameof(Queryable.Sum), typeof(int))] = new Func<IQueryable<int>, Expression<Func<int, int>>, int>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(long))] = new Func<IQueryable<long>, Expression<Func<long, long>>, long>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(float))] = new Func<IQueryable<float>, Expression<Func<float, float>>, float>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(double))] = new Func<IQueryable<double>, Expression<Func<double, double>>, double>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(decimal))] = new Func<IQueryable<decimal>, Expression<Func<decimal, decimal>>, decimal>(Queryable.Sum).Method,
		[(nameof(Queryable.Average), typeof(int))] = new Func<IQueryable<int>, Expression<Func<int, int>>, double>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(long))] = new Func<IQueryable<long>, Expression<Func<long, long>>, double>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(float))] = new Func<IQueryable<float>, Expression<Func<float, float>>, float>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(double))] = new Func<IQueryable<double>, Expression<Func<double, double>>, double>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(decimal))] = new Func<IQueryable<decimal>, Expression<Func<decimal, decimal>>, decimal>(Queryable.Average).Method,
		[(nameof(Queryable.Sum), typeof(int?))] = new Func<IQueryable<int?>, Expression<Func<int?, int?>>, int?>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(long?))] = new Func<IQueryable<long?>, Expression<Func<long?, long?>>, long?>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(float?))] = new Func<IQueryable<float?>, Expression<Func<float?, float?>>, float?>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(double?))] = new Func<IQueryable<double?>, Expression<Func<double?, double?>>, double?>(Queryable.Sum).Method,
		[(nameof(Queryable.Sum), typeof(decimal?))] = new Func<IQueryable<decimal?>, Expression<Func<decimal?, decimal?>>, decimal?>(Queryable.Sum).Method,
		[(nameof(Queryable.Average), typeof(int?))] = new Func<IQueryable<int?>, Expression<Func<int?, int?>>, double?>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(long?))] = new Func<IQueryable<long?>, Expression<Func<long?, long?>>, double?>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(float?))] = new Func<IQueryable<float?>, Expression<Func<float?, float?>>, float?>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(double?))] = new Func<IQueryable<double?>, Expression<Func<double?, double?>>, double?>(Queryable.Average).Method,
		[(nameof(Queryable.Average), typeof(decimal?))] = new Func<IQueryable<decimal?>, Expression<Func<decimal?, decimal?>>, decimal?>(Queryable.Average).Method
	};

	private static readonly MethodInfo MinWithSelector =
		new Func<IQueryable<object>, Expression<Func<object, object>>, object?>(Queryable.Min).Method.GetGenericMethodDefinition();

	private static readonly MethodInfo MaxWithSelector =
		new Func<IQueryable<object>, Expression<Func<object, object>>, object?>(Queryable.Max).Method.GetGenericMethodDefinition();

	private static readonly ConcurrentDictionary<Type, (Type Row, PropertyInfo Result)> RowTypes = new();

	// The expression that stands for the single value of each row after a scalar Select, by the call that produces those rows.
	private readonly Dictionary<Expression, SingleValue> _singleValues = [];

	// The single aggregations that follow a GroupBy.
	private readonly HashSet<Expression> _groupedAggregations = [];

	private readonly record struct SingleValue(LambdaExpression Selector, Expression Column);

	/// <summary>A selector returning a single value: a field, or a value computed from the row.</summary>
	public static bool IsScalarSelector(LambdaExpression lambda) =>
		lambda.Parameters.Count == 1
		&& TypeHelper.IsSingleValueType(lambda.ReturnType)
		&& lambda.Body is not NewExpression and not MemberInitExpression
		&& lambda.Body.UnwrapConvertExpressions() != lambda.Parameters[0];

	/// <summary>A single-value selector that computes its value rather than reading a field.</summary>
	public static bool IsComputed(LambdaExpression singleValueSelector) => !IsFieldPath(singleValueSelector.Body);

	/// <summary>
	/// Wraps a computed single-value selector into <c>new ScalarRow&lt;T&gt; { Result = ... }</c>,
	/// which projects into the <c>result</c> column.
	/// </summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The wrapped selector is translated to ES|QL, never compiled into a delegate.")]
	public static LambdaExpression WrapComputedSelector(LambdaExpression lambda)
	{
		var (row, result) = RowOf(lambda.ReturnType);
		return Expression.Lambda(Expression.MemberInit(Expression.New(row), Expression.Bind(result, lambda.Body)), lambda.Parameters);
	}

	// The ScalarRow<T> for a value type, with its Result property, resolved once per type.
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The wrapper is closed over a type taken from the existing expression tree.")]
	private static (Type Row, PropertyInfo Result) RowOf(Type valueType)
	{
		if (RowTypes.TryGetValue(valueType, out var known))
			return known;

		var row = typeof(ScalarRow<>).MakeGenericType(valueType);
		var result = row.GetProperty(nameof(ScalarRow<>.Result)) ?? throw new InvalidOperationException("The scalar row has no Result property.");
		return RowTypes.GetOrAdd(valueType, (row, result));
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	protected override Expression VisitMethodCall(MethodCallExpression node)
	{
		var visited = (MethodCallExpression)base.VisitMethodCall(node);

		if (visited.Method.DeclaringType != typeof(Queryable) || visited.Arguments.Count == 0)
			return visited;

		if (_singleValues.TryGetValue(visited.Arguments[0], out var value))
			return TranslatedOperators.Contains(visited.Method.Name) ? Rewrite(visited, value) : visited;

		// A single aggregation after a GroupBy is translated into STATS beside the key, under the name of its method,
		// so a translated operator that reads its value has no column to name. The others leave it as it is.
		if (_groupedAggregations.Contains(visited.Arguments[0]))
		{
			if (TranslatedOperators.Contains(visited.Method.Name) && ReadsTheValue(visited))
			{
				throw new NotSupportedException(
					$"A single aggregation after GroupBy cannot be followed by {visited.Method.Name}, which reads its value: "
					+ "project it into a member, as in 'g => new { Count = g.Count() }', and read the member.");
			}

			if (RowPreservingOperators.Contains(visited.Method.Name))
				_ = _groupedAggregations.Add(visited);

			return visited;
		}

		if (visited.Method.Name == nameof(Queryable.Select) && ExtractLambda(visited) is { } selector && IsScalarSelector(selector))
		{
			// Only an aggregation call: any other selector after a GroupBy is refused there with a message of its own.
			if (visited.Arguments[0] is MethodCallExpression { Method.Name: nameof(Queryable.GroupBy) })
			{
				if (selector.Body.UnwrapConvertExpressions() is MethodCallExpression)
					_ = _groupedAggregations.Add(visited);
			}
			else
				_singleValues[visited] = new SingleValue(selector, ColumnOf(selector));
		}

		return visited;
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	private Expression Rewrite(MethodCallExpression node, SingleValue value)
	{
		var name = node.Method.Name;

		// A Select right after the scalar one reads the value through the selector that produced it, so the two fold into one.
		if (name == nameof(Queryable.Select) && ExtractLambda(node) is { } outer
			&& node.Arguments[0] is MethodCallExpression inner && ExtractLambda(inner) == value.Selector)
		{
			var composed = Expression.Lambda(Substitute(outer, value.Selector.Body), value.Selector.Parameters);
			var source = inner.Arguments[0];
			var method = node.Method.GetGenericMethodDefinition()
				.MakeGenericMethod(value.Selector.Parameters[0].Type, outer.ReturnType);
			var folded = Expression.Call(method, source, Expression.Quote(composed));
			if (IsScalarSelector(composed))
				_singleValues[folded] = new SingleValue(composed, ColumnOf(composed));
			return folded;
		}

		// Sum(), Max() and the like name no field: they aggregate the single value.
		if (SelectorAggregates.Contains(name) && node.Arguments.Count == 1)
		{
			var elementType = value.Selector.ReturnType;
			var parameter = Expression.Parameter(elementType, "x");
			var selector = Expression.Lambda(value.Column, parameter);
			return Expression.Call(FindSelectorOverload(node.Method, elementType), node.Arguments[0], Expression.Quote(selector));
		}

		var arguments = node.Arguments
			.Select(a => a is UnaryExpression { Operand: LambdaExpression lambda } && lambda.Parameters.Count == 1
				? Expression.Quote(Expression.Lambda(lambda.Type, Substitute(lambda, value.Column), lambda.Parameters))
				: a)
			.ToList();
		var rewritten = node.Update(node.Object, arguments);

		if (RowPreservingOperators.Contains(name))
			_singleValues[rewritten] = value;

		// A Select further down that again returns a single value leaves a column of its own.
		else if (name == nameof(Queryable.Select) && ExtractLambda(rewritten) is { } selector && IsScalarSelector(selector))
			_singleValues[rewritten] = new SingleValue(selector, ColumnOf(selector));

		return rewritten;
	}

	// An operator that takes a lambda over the row, or an aggregate that, without one, aggregates the row itself.
	private static bool ReadsTheValue(MethodCallExpression node) =>
		node.Arguments.Skip(1).Any(a => a is UnaryExpression { Operand: LambdaExpression })
		|| (SelectorAggregates.Contains(node.Method.Name) && node.Arguments.Count == 1);

	private static Expression ColumnOf(LambdaExpression selector)
	{
		if (IsFieldPath(selector.Body))
			return selector.Body;

		// The column is the Result member of the row the wrapped selector projects into.
		var (row, result) = RowOf(selector.ReturnType);
		return Expression.MakeMemberAccess(Expression.Parameter(row, "row"), result);
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Generic method instantiation uses types from the existing expression tree.")]
	private static MethodInfo FindSelectorOverload(MethodInfo parameterless, Type elementType) =>
		parameterless.Name switch
		{
			nameof(Queryable.Min) => MinWithSelector.MakeGenericMethod(elementType, elementType),
			nameof(Queryable.Max) => MaxWithSelector.MakeGenericMethod(elementType, elementType),
			_ when NumericSelectorAggregates.TryGetValue((parameterless.Name, elementType), out var method) => method,
			_ => throw new NotSupportedException($"{parameterless.Name} over a {elementType.Name} is not supported.")
		};

	private static Expression Substitute(LambdaExpression lambda, Expression replacement) =>
		new ParameterReplacer(lambda.Parameters[0], replacement).Visit(lambda.Body);

	private static LambdaExpression? ExtractLambda(MethodCallExpression node) =>
		node.Arguments.Count >= 2 && node.Arguments[1] is UnaryExpression { Operand: LambdaExpression lambda } ? lambda : null;

	// A path of document members down from a row. The row is the selector's own parameter, or the one an earlier
	// Select read the field from, once an operator after it has been rewritten. A member of a value, such as
	// DateTime.Hour or string.Length, ends the path: the value is computed from the field rather than read as one.
	private static bool IsFieldPath(Expression body)
	{
		var current = body.UnwrapConvertExpressions();
		if (current is not MemberExpression)
			return false;

		while (current is MemberExpression member)
		{
			var parent = member.Expression?.UnwrapConvertExpressions();
			if (parent is null || TypeHelper.IsSingleValueType(parent.Type))
				return false;
			current = parent;
		}

		return current is ParameterExpression;
	}

	private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
	{
		protected override Expression VisitParameter(ParameterExpression node) =>
			node == parameter ? replacement : base.VisitParameter(node);
	}
}

/// <summary>The row a computed scalar Select projects: its single value in the <c>result</c> column.</summary>
/// <remarks>
/// Marked as compiler-generated so that its member is named like the member of an anonymous type,
/// through the naming policy, since no serializer context of the caller knows this type.
/// </remarks>
[CompilerGenerated]
internal sealed class ScalarRow<T>
{
	public T Result { get; set; } = default!;
}
