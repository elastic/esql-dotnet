// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql;
using Elastic.Esql.Execution;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using HttpMethod = Elastic.Transport.HttpMethod;
#if NET10_0_OR_GREATER
using System.IO.Pipelines;
#endif

namespace Elastic.Clients.Esql.Execution;

/// <summary>Executes ES|QL queries against Elasticsearch via HTTP transport.</summary>
internal sealed class EsqlTransportExecutor(EsqlClientSettings settings) : IEsqlQueryExecutor
{
	private readonly EsqlClientSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

	private static readonly EndpointPath QueryEndpoint = new(HttpMethod.POST, "/_query");
	private static readonly EndpointPath AsyncQueryEndpoint = new(HttpMethod.POST, "/_query/async");

	public IEsqlResponse ExecuteQuery(string esql, EsqlQueryOptions? options)
	{
		var postData = BuildPostData(esql, options);
		var response = _settings.Transport.Request<StreamResponse>(in QueryEndpoint, postData, null, null);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> ExecuteQueryAsync(
		string esql,
		EsqlQueryOptions? options,
		CancellationToken cancellationToken)
	{
		var postData = BuildPostData(esql, options);

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in QueryEndpoint, postData, null, null, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL query failed", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in QueryEndpoint, postData, null, null, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse SubmitAsyncQuery(string esql, EsqlAsyncQueryOptions? options)
	{
		var (postData, endpoint) = BuildAsyncPostData(esql, options);
		var response = _settings.Transport.Request<StreamResponse>(in endpoint, postData, null, null);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(
		string esql,
		EsqlAsyncQueryOptions? options,
		CancellationToken cancellationToken)
	{
		var (postData, endpoint) = BuildAsyncPostData(esql, options);

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in endpoint, postData, null, null, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL async query failed", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpoint, postData, null, null, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse PollAsyncQuery(string queryId)
	{
		var endpointPath = new EndpointPath(HttpMethod.GET, $"/_query/async/{queryId}");
		var response = _settings.Transport.Request<StreamResponse>(in endpointPath, null, null, null);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, CancellationToken cancellationToken)
	{
		var endpointPath = new EndpointPath(HttpMethod.GET, $"/_query/async/{queryId}");

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in endpointPath, null, null, null, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "Failed to get async query status", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpointPath, null, null, null, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public void DeleteAsyncQuery(string queryId)
	{
		var endpointPath = new EndpointPath(HttpMethod.DELETE, $"/_query/async/{queryId}");
		using var response = _settings.Transport.Request<StreamResponse>(in endpointPath, null, null, null);
		ThrowIfError(response, "Failed to delete async query");
	}

	public async Task DeleteAsyncQueryAsync(string queryId, CancellationToken cancellationToken)
	{
		var endpointPath = new EndpointPath(HttpMethod.DELETE, $"/_query/async/{queryId}");
		using var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpointPath, null, null, null, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "Failed to delete async query");
	}

	private static void ThrowIfError(StreamResponse response, string operation)
	{
		if (response.ApiCallDetails.HasSuccessfulStatusCode)
			return;

		var statusCode = response.ApiCallDetails.HttpStatusCode;
		var message = $"{operation}: {statusCode}";
		string? responseBody = null;

		try
		{
			if (ElasticsearchServerError.TryCreate(response.Body, out var serverError) && serverError?.HasError() == true)
			{
				message = $"{operation}: {serverError.Error}";
				responseBody = serverError.ToString();
			}
		}
		catch
		{
			// Don't mask the original HTTP error
		}

		response.Dispose();
		throw new EsqlExecutionException(message, responseBody, statusCode);
	}

#if NET10_0_OR_GREATER
	private static async Task ThrowIfErrorAsync(PipeResponse response, string operation, CancellationToken ct = default)
	{
		if (response.ApiCallDetails.HasSuccessfulStatusCode)
			return;

		var statusCode = response.ApiCallDetails.HttpStatusCode;
		var message = $"{operation}: {statusCode}";
		string? responseBody = null;

		try
		{
			var serverError = await ElasticsearchServerError
				.CreateAsync(response.Body.AsStream(), ct)
				.ConfigureAwait(false);
			if (serverError?.HasError() == true)
			{
				message = $"{operation}: {serverError.Error}";
				responseBody = serverError.ToString();
			}
		}
		catch
		{
			// Don't mask the original HTTP error
		}

		await response.DisposeAsync().ConfigureAwait(false);
		throw new EsqlExecutionException(message, responseBody, statusCode);
	}
#endif

	private PostData BuildPostData(string esql, EsqlQueryOptions? options)
	{
		var request = BuildRequest(esql, options);
		var json = JsonSerializer.Serialize(request, EsqlRequestJsonContext.Default.EsqlRequest);
		return PostData.String(json);
	}

	private (PostData Data, EndpointPath Endpoint) BuildAsyncPostData(string esql, EsqlAsyncQueryOptions? options)
	{
		var request = BuildAsyncRequest(esql, options);
		var endpoint = BuildAsyncQueryEndpoint(request);
		var json = JsonSerializer.Serialize(request, EsqlRequestJsonContext.Default.EsqlAsyncRequest);
		return (PostData.String(json), endpoint);
	}

	private EsqlRequest BuildRequest(string esql, EsqlQueryOptions? options)
	{
		var defaults = _settings.Defaults;
		return new EsqlRequest
		{
			Query = esql,
			Locale = options?.Locale ?? defaults.Locale,
			TimeZone = options?.TimeZone ?? defaults.TimeZone,
			Params = options?.Parameters
		};
	}

	private EsqlAsyncRequest BuildAsyncRequest(string esql, EsqlAsyncQueryOptions? options)
	{
		var defaults = _settings.Defaults;
		return new EsqlAsyncRequest
		{
			Query = esql,
			Locale = options?.Locale ?? defaults.Locale,
			TimeZone = options?.TimeZone ?? defaults.TimeZone,
			Params = options?.Parameters,
			WaitForCompletionTimeout = options?.WaitForCompletionTimeout,
			KeepAlive = options?.KeepAlive,
			KeepOnCompletion = options?.KeepOnCompletion ?? false
		};
	}

	private static EndpointPath BuildAsyncQueryEndpoint(EsqlAsyncRequest request)
	{
		var queryParams = new List<string>();
		if (request.WaitForCompletionTimeout.HasValue)
			queryParams.Add($"wait_for_completion_timeout={FormatTimeSpan(request.WaitForCompletionTimeout.Value)}");
		if (request.KeepAlive.HasValue)
			queryParams.Add($"keep_alive={FormatTimeSpan(request.KeepAlive.Value)}");
		if (request.KeepOnCompletion)
			queryParams.Add("keep_on_completion=true");

		return queryParams.Count > 0
			? new EndpointPath(HttpMethod.POST, $"/_query/async?{string.Join("&", queryParams)}")
			: AsyncQueryEndpoint;
	}

	private static string FormatTimeSpan(TimeSpan ts) =>
		ts.TotalMilliseconds < 1000 ? $"{(int)ts.TotalMilliseconds}ms" : $"{(int)ts.TotalSeconds}s";
}

/// <summary>Wraps a <see cref="StreamResponse"/> as an <see cref="IEsqlResponse"/>.</summary>
internal sealed class TransportEsqlResponse(StreamResponse response) : IEsqlResponse
{
	public Stream Body => response.Body;

	public void Dispose() => response.Dispose();
}

#if NET10_0_OR_GREATER
/// <summary>Wraps a <see cref="PipeResponse"/> as an <see cref="IEsqlAsyncResponse"/>, using its native <see cref="PipeReader"/>.</summary>
internal sealed class TransportEsqlAsyncResponse(PipeResponse response) : IEsqlAsyncResponse
{
	public PipeReader Body => response.Body;

	public async ValueTask DisposeAsync() =>
		await response.DisposeAsync().ConfigureAwait(false);
}
#else
/// <summary>Wraps a <see cref="StreamResponse"/> as an <see cref="IEsqlAsyncResponse"/>.</summary>
internal sealed class TransportEsqlAsyncResponse(StreamResponse response) : IEsqlAsyncResponse
{
	public Stream Body => response.Body;

	public ValueTask DisposeAsync()
	{
		response.Dispose();
		return default;
	}
}
#endif
