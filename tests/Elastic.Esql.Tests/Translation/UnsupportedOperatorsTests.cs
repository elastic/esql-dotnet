// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;
using System.Reflection;

namespace Elastic.Esql.Tests.Translation;

public class UnsupportedOperatorsTests : EsqlTestBase
{
	[Test]
	public void TakeWhile_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.TakeWhile(l => l.StatusCode > 100)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*TakeWhile*");
	}

	[Test]
	public void SkipWhile_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.SkipWhile(l => l.StatusCode > 100)
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*SkipWhile*");
	}

	[Test]
	public void Last_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.Last();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*Last*");
	}

	[Test]
	public void UnknownCustomOperator_ThrowsNotSupported()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.CustomPassthrough()
			.ToString();

		_ = act.Should().Throw<NotSupportedException>()
			.WithMessage("*CustomPassthrough*");
	}
}

internal static class UnsupportedOperatorsTestExtensions
{
	private static readonly MethodInfo CustomPassthroughMethod =
		new Func<IQueryable<object>, IQueryable<object>>(CustomPassthrough)
			.Method
			.GetGenericMethodDefinition();

	public static IQueryable<T> CustomPassthrough<T>(this IQueryable<T> source)
	{
		var method = CustomPassthroughMethod.MakeGenericMethod(typeof(T));
		return source.Provider.CreateQuery<T>(Expression.Call(instance: null, method, source.Expression));
	}
}
