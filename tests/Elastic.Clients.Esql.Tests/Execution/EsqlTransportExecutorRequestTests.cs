// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Clients.Esql.Execution;
using Elastic.Esql;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;
using Elastic.Transport;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Elastic.Clients.Esql.Tests.Execution;

/// <summary>
/// Characterizes the wire shape (endpoint, query string, body, headers) the transport executor
/// produces for each request kind, using an in-memory transport double.
/// </summary>
public class EsqlTransportExecutorRequestTests
{
	[Test]
	public void ExecuteQuery_NoOptions_PostsQueryBodyToQueryEndpoint()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest { Esql = "FROM logs-*" });

		_ = invoker.LastEndpoint!.Method.Should().Be(HttpMethod.POST);
		_ = invoker.LastEndpoint.PathAndQuery.Should().Be("/_query");
		_ = invoker.LastRequestBody.Should().Be("""{"query":"FROM logs-*"}""");
	}

	[Test]
	public void ExecuteQuery_NoFormat_UsesElasticsearchDefaultAccept()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest { Esql = "FROM logs-*" });

		_ = invoker.LastBoundConfiguration!.Accept.Should().Be("application/vnd.elasticsearch+json");
	}

	[Test]
	public void ExecuteQuery_FormatAndFlags_AppendsQueryStringParameters()
	{
		var (executor, invoker) = TestExecutorFactory.Create();
		var options = new EsqlQueryOptions { AllowPartialResults = false, DropNullColumns = true };

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest
		{
			Esql = "FROM logs-*",
			QueryOptions = options,
			Format = EsqlFormat.Csv
		});

		_ = invoker.LastEndpoint!.PathAndQuery.Should().Be("/_query?allow_partial_results=false&drop_null_columns=true&format=csv");
	}

	[Test]
	public void ExecuteQuery_CsvFormat_SetsMatchingAcceptHeader()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest { Esql = "FROM logs-*", Format = EsqlFormat.Csv });

		_ = invoker.LastEndpoint!.PathAndQuery.Should().Be("/_query?format=csv");
		_ = invoker.LastBoundConfiguration!.Accept.Should().Be("text/csv");
	}

	[Test]
	public void ExecuteQuery_UserAcceptConfigured_PreservesUserAccept()
	{
		var (executor, invoker) = TestExecutorFactory.Create();
		var transportOptions = new EsqlTransportOptions
		{
			RequestConfiguration = new RequestConfiguration { Accept = "application/custom" }
		};

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest
		{
			Esql = "FROM logs-*",
			ExecutorOptions = transportOptions,
			Format = EsqlFormat.Csv
		});

		_ = invoker.LastBoundConfiguration!.Accept.Should().Be("application/custom");
	}

	[Test]
	public void ExecuteQuery_DefaultsConfigured_AppliesLocaleAndTimeZone()
	{
		var (executor, invoker) = TestExecutorFactory.Create(
			defaults: new EsqlQueryDefaults { Locale = "en-US", TimeZone = "UTC" });

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest { Esql = "FROM logs-*" });

		_ = invoker.LastRequestBody.Should().Be("""{"query":"FROM logs-*","locale":"en-US","time_zone":"UTC"}""");
	}

	[Test]
	public void ExecuteQuery_PerQueryOptions_OverrideDefaults()
	{
		var (executor, invoker) = TestExecutorFactory.Create(
			defaults: new EsqlQueryDefaults { Locale = "en-US", TimeZone = "UTC" });

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest
		{
			Esql = "FROM logs-*",
			QueryOptions = new EsqlQueryOptions { TimeZone = "Europe/Berlin" }
		});

		_ = invoker.LastRequestBody.Should().Be("""{"query":"FROM logs-*","locale":"en-US","time_zone":"Europe/Berlin"}""");
	}

	[Test]
	public void ExecuteQuery_Parameters_SerializedAsNamedParamsList()
	{
		var (executor, invoker) = TestExecutorFactory.Create();
		var parameters = new EsqlParameters();
		_ = parameters.Add("level", JsonSerializer.SerializeToElement("ERROR"));
		_ = parameters.Add("code", JsonSerializer.SerializeToElement(500));

		using var response = executor.ExecuteQuery(new EsqlExecutionRequest
		{
			Esql = "FROM logs-* | WHERE log.level == ?level",
			Parameters = parameters
		});

		_ = invoker.LastRequestBody.Should().Be(
			"""{"query":"FROM logs-* | WHERE log.level == ?level","params":[{"level":"ERROR"},{"code":500}]}""");
	}

	[Test]
	public void SubmitAsyncQuery_AsyncOptions_PostsAsyncBodyToAsyncEndpoint()
	{
		var (executor, invoker) = TestExecutorFactory.Create();
		var asyncOptions = new EsqlAsyncQueryOptions
		{
			WaitForCompletionTimeout = TimeSpan.FromSeconds(2),
			KeepAlive = TimeSpan.FromDays(5),
			KeepOnCompletion = true
		};

		using var response = executor.SubmitAsyncQuery(new EsqlExecutionRequest { Esql = "FROM logs-*", AsyncOptions = asyncOptions });

		_ = invoker.LastEndpoint!.Method.Should().Be(HttpMethod.POST);
		_ = invoker.LastEndpoint.PathAndQuery.Should().Be("/_query/async");
		_ = invoker.LastRequestBody.Should().Be(
			"""{"query":"FROM logs-*","wait_for_completion_timeout":"2s","keep_alive":"5d","keep_on_completion":true}""");
	}

	[Test]
	public void SubmitAsyncQuery_Always_RequestsAsyncResponseHeaders()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		using var response = executor.SubmitAsyncQuery(new EsqlExecutionRequest { Esql = "FROM logs-*" });

		var headers = invoker.LastBoundConfiguration!.ResponseHeadersToParse;
		_ = headers.HasValue.Should().BeTrue();

		var headerNames = headers!.Value.ToList();
		_ = headerNames.Should().Contain("X-Elasticsearch-Async-Id");
		_ = headerNames.Should().Contain("X-Elasticsearch-Async-Is-Running");
	}

	[Test]
	public void PollAsyncQuery_FormatAndSpecialCharacters_EscapesIdAndAppendsFormat()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		using var response = executor.PollAsyncQuery("id/with:special chars", new EsqlExecutionRequest { Esql = "", Format = EsqlFormat.Arrow });

		_ = invoker.LastEndpoint!.Method.Should().Be(HttpMethod.GET);
		_ = invoker.LastEndpoint.PathAndQuery.Should().Be("/_query/async/id%2Fwith%3Aspecial%20chars?format=arrow");
		_ = invoker.LastBoundConfiguration!.Accept.Should().Be("application/vnd.apache.arrow.stream");
	}

	[Test]
	public void PollAsyncQuery_EmptyQueryId_ThrowsArgumentException()
	{
		var (executor, _) = TestExecutorFactory.Create();

		var act = () => executor.PollAsyncQuery("", new EsqlExecutionRequest { Esql = "" });

		_ = act.Should().Throw<ArgumentException>();
	}

	[Test]
	public void DeleteAsyncQuery_QueryId_IssuesDeleteToAsyncEndpoint()
	{
		var (executor, invoker) = TestExecutorFactory.Create();

		executor.DeleteAsyncQuery("abc123", new EsqlExecutionRequest { Esql = "" });

		_ = invoker.LastEndpoint!.Method.Should().Be(HttpMethod.DELETE);
		_ = invoker.LastEndpoint.PathAndQuery.Should().Be("/_query/async/abc123");
	}

	[Test]
	public void ExecuteQuery_ErrorStatusCode_ThrowsEsqlExecutionException()
	{
		var (executor, _) = TestExecutorFactory.Create(
			responseBody: """{"error":{"type":"parsing_exception","reason":"bad query"}}"""u8.ToArray(),
			statusCode: 400);

		var act = () =>
		{
			using var response = executor.ExecuteQuery(new EsqlExecutionRequest { Esql = "FROM nope" });
		};

		_ = act.Should().Throw<EsqlExecutionException>()
			.Which.StatusCode.Should().Be(400);
	}
}
