// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Elastic.Esql.Execution;

/// <summary>
/// Executes ES|QL queries against Elasticsearch.
/// </summary>
public class EsqlExecutor(EsqlClientSettings settings)
{
	private readonly EsqlClientSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

	private static readonly EndpointPath QueryEndpoint = new(HttpMethod.POST, "/_query");

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Executes an ES|QL query and returns the response.
	/// </summary>
	public async Task<EsqlResponse> ExecuteAsync(string query, CancellationToken cancellationToken = default)
	{
		var request = new EsqlRequest
		{
			Query = query,
			Columnar = _settings.Columnar,
			Profile = _settings.IncludeProfile
		};

		return await ExecuteAsync(request, cancellationToken);
	}

	/// <summary>
	/// Executes an ES|QL request and returns the response.
	/// </summary>
	public async Task<EsqlResponse> ExecuteAsync(EsqlRequest request, CancellationToken cancellationToken = default)
	{
		var json = JsonSerializer.Serialize(request, JsonOptions);
		var postData = PostData.String(json);

		var response = await _settings.Transport.RequestAsync<StringResponse>(
			in QueryEndpoint,
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

	/// <summary>
	/// Gets the status of an async query.
	/// </summary>
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

	/// <summary>
	/// Deletes an async query.
	/// </summary>
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
}

/// <summary>
/// Exception thrown when ES|QL query execution fails.
/// </summary>
public class EsqlExecutionException : Exception
{
	/// <summary>
	/// The response body from Elasticsearch.
	/// </summary>
	public string? ResponseBody { get; }

	/// <summary>
	/// The HTTP status code.
	/// </summary>
	public int? StatusCode { get; }

	public EsqlExecutionException(string message) : base(message)
	{
	}

	public EsqlExecutionException(string message, string? responseBody, int? statusCode)
		: base(message)
	{
		ResponseBody = responseBody;
		StatusCode = statusCode;
	}

	public EsqlExecutionException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
