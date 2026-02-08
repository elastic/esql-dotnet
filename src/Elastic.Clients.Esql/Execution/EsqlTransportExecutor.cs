// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Esql;
using Elastic.Esql.Execution;
using Elastic.Transport;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Elastic.Clients.Esql.Execution;

/// <summary>Executes ES|QL queries against Elasticsearch via HTTP transport.</summary>
public class EsqlTransportExecutor(EsqlClientSettings settings) : IEsqlQueryExecutor
{
	private readonly EsqlClientSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

	private static readonly EndpointPath QueryEndpoint = new(HttpMethod.POST, "/_query");
	private static readonly EndpointPath AsyncQueryEndpoint = new(HttpMethod.POST, "/_query/async");

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <inheritdoc/>
	public async Task<EsqlResponse> ExecuteAsync(string esql, CancellationToken cancellationToken = default) =>
		await ExecuteAsync(esql, (EsqlQueryOptions?)null, cancellationToken);

	/// <inheritdoc/>
	public async Task<EsqlResponse> ExecuteAsync(string esql, IReadOnlyList<object>? parameters, CancellationToken cancellationToken = default) =>
		await ExecuteAsync(esql, parameters != null ? new EsqlQueryOptions { Parameters = parameters } : null, cancellationToken);

	/// <summary>Executes an ES|QL query with options and returns the response.</summary>
	public async Task<EsqlResponse> ExecuteAsync(string esql, EsqlQueryOptions? options, CancellationToken cancellationToken = default)
	{
		var defaults = _settings.Defaults;
		var request = new EsqlRequest
		{
			Query = esql,
			Columnar = options?.Columnar ?? defaults.Columnar,
			Profile = options?.IncludeProfile ?? defaults.IncludeProfile,
			Locale = options?.Locale ?? defaults.Locale,
			TimeZone = options?.TimeZone ?? defaults.TimeZone,
			Params = options?.Parameters?.ToList()
		};

		return await ExecuteAsync(request, cancellationToken);
	}

	/// <summary>Executes an ES|QL request and returns the response.</summary>
	public async Task<EsqlResponse> ExecuteAsync(EsqlRequest request, CancellationToken cancellationToken = default) =>
		await ExecuteEndpointAsync(QueryEndpoint, request, cancellationToken);

	/// <summary>Executes an async ES|QL query and returns a wrapper for managing it.</summary>
	public async Task<EsqlAsyncQuery<T>> ExecuteAsyncAsync<T>(EsqlAsyncRequest request, CancellationToken cancellationToken = default)
	{
		var queryParams = new List<string>();
		if (request.WaitForCompletionTimeout.HasValue)
			queryParams.Add($"wait_for_completion_timeout={FormatTimeSpan(request.WaitForCompletionTimeout.Value)}");
		if (request.KeepAlive.HasValue)
			queryParams.Add($"keep_alive={FormatTimeSpan(request.KeepAlive.Value)}");
		if (request.KeepOnCompletion)
			queryParams.Add("keep_on_completion=true");

		var endpoint = queryParams.Count > 0
			? new EndpointPath(HttpMethod.POST, $"/_query/async?{string.Join("&", queryParams)}")
			: AsyncQueryEndpoint;

		var response = await ExecuteEndpointAsync(endpoint, request, cancellationToken);
		return new EsqlAsyncQuery<T>(this, response, new Elastic.Mapping.TypeFieldMetadataResolver(_settings.MappingContext));
	}

	/// <summary>Gets the status of an async query.</summary>
	public async Task<EsqlResponse> GetAsyncStatusAsync(string queryId, CancellationToken cancellationToken = default)
	{
		var endpointPath = new EndpointPath(HttpMethod.GET, $"/_query/async/{queryId}");

		var response = await _settings.Transport.RequestAsync<StringResponse>(
			in endpointPath,
			null,
			null,
			null,
			cancellationToken);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
		{
			throw new EsqlExecutionException(
				$"Failed to get async query status: {response.ApiCallDetails.HttpStatusCode}",
				response.Body,
				response.ApiCallDetails.HttpStatusCode);
		}

		var esqlResponse = JsonSerializer.Deserialize<EsqlResponse>(response.Body, JsonOptions);
		return esqlResponse ?? new EsqlResponse();
	}

	/// <summary>Deletes an async query.</summary>
	public async Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken = default)
	{
		var endpointPath = new EndpointPath(HttpMethod.DELETE, $"/_query/async/{queryId}");

		var response = await _settings.Transport.RequestAsync<StringResponse>(
			in endpointPath,
			null,
			null,
			null,
			cancellationToken);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
		{
			throw new EsqlExecutionException(
				$"Failed to delete async query: {response.ApiCallDetails.HttpStatusCode}",
				response.Body,
				response.ApiCallDetails.HttpStatusCode);
		}
	}

	private async Task<EsqlResponse> ExecuteEndpointAsync(EndpointPath endpoint, EsqlRequest request, CancellationToken cancellationToken)
	{
		var json = JsonSerializer.Serialize(request, JsonOptions);
		var postData = PostData.String(json);

		var response = await _settings.Transport.RequestAsync<StringResponse>(
			in endpoint,
			postData,
			null,
			null,
			cancellationToken);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
		{
			throw new EsqlExecutionException(
				$"ES|QL query failed: {response.ApiCallDetails.HttpStatusCode}",
				response.Body,
				response.ApiCallDetails.HttpStatusCode);
		}

		var esqlResponse = JsonSerializer.Deserialize<EsqlResponse>(response.Body, JsonOptions);
		return esqlResponse ?? new EsqlResponse();
	}

	private static string FormatTimeSpan(TimeSpan ts) =>
		ts.TotalMilliseconds < 1000 ? $"{(int)ts.TotalMilliseconds}ms" : $"{(int)ts.TotalSeconds}s";
}
