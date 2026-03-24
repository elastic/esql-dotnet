// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql.QueryModel;
using Elastic.Esql.QueryModel.Commands;

namespace Elastic.Esql.Tests.Interception;

public class QueryInterceptorTests
{
	private static EsqlQueryProvider CreateProvider(IEsqlQueryInterceptor? interceptor = null) =>
		new(
			new JsonSerializerOptions
			{
				TypeInfoResolver = EsqlTestMappingContext.Default,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}
		)
		{ Interceptor = interceptor };

	private static EsqlQueryable<T> CreateQuery<T>(IEsqlQueryInterceptor? interceptor = null) =>
		new(CreateProvider(interceptor));

	[Test]
	public void NoInterceptor_QueryUnchanged()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void Interceptor_PrependsFrom_WhenNoSource()
	{
		var interceptor = new SourceInferenceInterceptor("inferred-index");

		var esql = CreateQuery<LogEntry>(interceptor)
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM inferred-index
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void Interceptor_LeavesExistingFrom_Unchanged()
	{
		var interceptor = new SourceInferenceInterceptor("inferred-index");

		var esql = CreateQuery<LogEntry>(interceptor)
			.From("explicit-index")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM explicit-index
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void WithSource_ReplacesExistingFrom()
	{
		var interceptor = new ReplaceSourceInterceptor("replaced-index");

		var esql = CreateQuery<LogEntry>(interceptor)
			.From("original-index")
			.Where(l => l.StatusCode >= 500)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM replaced-index
			| WHERE statusCode >= 500
			""".NativeLineEndings());
	}

	[Test]
	public void WithLimit_AddsLimit_WhenNoneExists()
	{
		var interceptor = new DefaultLimitInterceptor(10000);

		var esql = CreateQuery<LogEntry>(interceptor)
			.From("logs-*")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 10000
			""".NativeLineEndings());
	}

	[Test]
	public void WithLimit_ReplacesExistingLimit()
	{
		var interceptor = new DefaultLimitInterceptor(10000);

		var esql = CreateQuery<LogEntry>(interceptor)
			.From("logs-*")
			.Take(5)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| LIMIT 10000
			""".NativeLineEndings());
	}

	[Test]
	public void WithCommands_AllowsArbitraryManipulation()
	{
		var interceptor = new AppendWhereInterceptor("statusCode > 0");

		var esql = CreateQuery<LogEntry>(interceptor)
			.From("logs-*")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-*
			| WHERE statusCode > 0
			""".NativeLineEndings());
	}

	[Test]
	public void Interceptor_CanInspectElementType()
	{
		Type? capturedType = null;
		var interceptor = new TypeCapturingInterceptor(t => capturedType = t);

		_ = CreateQuery<LogEntry>(interceptor)
			.From("logs-*")
			.ToString();

		_ = capturedType.Should().Be<LogEntry>();
	}

	[Test]
	public void Interceptor_AppliedToGetParameters()
	{
		var called = false;
		var interceptor = new TypeCapturingInterceptor(_ => called = true);

		var query = CreateQuery<LogEntry>(interceptor)
			.Where(l => l.StatusCode >= 500)
			.AsEsqlQueryable();

		_ = query.GetParameters();

		_ = called.Should().BeTrue();
	}

	[Test]
	public void Interceptor_NotAppliedToGetQueryOptions()
	{
		var called = false;
		var interceptor = new TypeCapturingInterceptor(_ => called = true);

		var query = CreateQuery<LogEntry>(interceptor)
			.From("logs-*")
			.AsEsqlQueryable();

		_ = query.GetQueryOptions();

		_ = called.Should().BeFalse();
	}

	private sealed class SourceInferenceInterceptor(string defaultIndex) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query)
		{
			if (query.Source is not null)
				return query;
			return query.WithSource(defaultIndex);
		}
	}

	private sealed class ReplaceSourceInterceptor(string newIndex) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query) =>
			query.WithSource(newIndex);
	}

	private sealed class DefaultLimitInterceptor(int limit) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query) =>
			query.WithLimit(limit);
	}

	private sealed class AppendWhereInterceptor(string condition) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query)
		{
			var commands = query.Commands.ToList();
			commands.Add(new WhereCommand(condition));
			return query.WithCommands(commands);
		}
	}

	private sealed class TypeCapturingInterceptor(Action<Type> capture) : IEsqlQueryInterceptor
	{
		public EsqlQuery Intercept(EsqlQuery query)
		{
			capture(query.ElementType);
			return query;
		}
	}
}
