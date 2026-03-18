// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Esql;
using Elastic.Esql.Execution;
using Elastic.Esql.QueryModel;
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

	public IEsqlResponse ExecuteQuery(string esql, EsqlParameters? parameters, object? options)
	{
		var typedOptions = ResolveOptions(options);
		var postData = BuildPostData(esql, parameters, typedOptions);
		var response = _settings.Transport.Request<StreamResponse>(in QueryEndpoint, postData, null, typedOptions?.RequestConfiguration);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> ExecuteQueryAsync(
		string esql,
		EsqlParameters? parameters,
		object? options,
		CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(options);
		var postData = BuildPostData(esql, parameters, typedOptions);
		var requestConfig = typedOptions?.RequestConfiguration;

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in QueryEndpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL query failed", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in QueryEndpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse SubmitAsyncQuery(string esql, EsqlParameters? parameters, object? options, EsqlAsyncQueryOptions? asyncOptions)
	{
		var typedOptions = ResolveOptions(options);
		var (postData, endpoint) = BuildAsyncPostData(esql, parameters, typedOptions, asyncOptions);
		var response = _settings.Transport.Request<StreamResponse>(in endpoint, postData, null, typedOptions?.RequestConfiguration);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(
		string esql,
		EsqlParameters? parameters,
		object? options,
		EsqlAsyncQueryOptions? asyncOptions,
		CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(options);
		var (postData, endpoint) = BuildAsyncPostData(esql, parameters, typedOptions, asyncOptions);
		var requestConfig = typedOptions?.RequestConfiguration;

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL async query failed", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse PollAsyncQuery(string queryId, object? options)
	{
		var typedOptions = ResolveOptions(options);
		var endpointPath = new EndpointPath(HttpMethod.GET, $"/_query/async/{queryId}");
		var response = _settings.Transport.Request<StreamResponse>(in endpointPath, null, null, typedOptions?.RequestConfiguration);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(options);
		var endpointPath = new EndpointPath(HttpMethod.GET, $"/_query/async/{queryId}");
		var requestConfig = typedOptions?.RequestConfiguration;

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<PipeResponse>(in endpointPath, null, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "Failed to get async query status", cancellationToken).ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpointPath, null, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public void DeleteAsyncQuery(string queryId, object? options)
	{
		var typedOptions = ResolveOptions(options);
		var endpointPath = new EndpointPath(HttpMethod.DELETE, $"/_query/async/{queryId}");
		using var response = _settings.Transport.Request<StreamResponse>(in endpointPath, null, null, typedOptions?.RequestConfiguration);
		ThrowIfError(response, "Failed to delete async query");
	}

	public async Task DeleteAsyncQueryAsync(string queryId, object? options, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(options);
		var endpointPath = new EndpointPath(HttpMethod.DELETE, $"/_query/async/{queryId}");
		using var response = await _settings.Transport
			.RequestAsync<StreamResponse>(in endpointPath, null, null, typedOptions?.RequestConfiguration, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "Failed to delete async query");
	}

	private static EsqlQueryOptions? ResolveOptions(object? options)
	{
		if (options is null)
			return null;

		if (options is EsqlQueryOptions typed)
			return typed;

		throw new InvalidOperationException(
			$"Expected options of type '{nameof(EsqlQueryOptions)}' but received '{options.GetType().FullName}'.");
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

	private PostData BuildPostData(string esql, EsqlParameters? parameters, EsqlQueryOptions? options)
	{
		var request = BuildRequest(esql, parameters, options);
		return PostData.StreamHandler(
			request,
			static (req, stream) => JsonSerializer.Serialize(stream, req, EsqlRequestJsonContext.Default.EsqlRequest),
			static async (req, stream, ct) =>
				await JsonSerializer.SerializeAsync(stream, req, EsqlRequestJsonContext.Default.EsqlRequest, ct).ConfigureAwait(false)
		);
	}

	private (PostData Data, EndpointPath Endpoint) BuildAsyncPostData(
		string esql,
		EsqlParameters? parameters,
		EsqlQueryOptions? options,
		EsqlAsyncQueryOptions? asyncOptions)
	{
		var request = BuildAsyncRequest(esql, parameters, options, asyncOptions);
		var endpoint = BuildAsyncQueryEndpoint(request);
		var postData = PostData.StreamHandler(
			request,
			static (req, stream) => JsonSerializer.Serialize(stream, req, EsqlRequestJsonContext.Default.EsqlAsyncRequest),
			static async (req, stream, ct) =>
				await JsonSerializer.SerializeAsync(stream, req, EsqlRequestJsonContext.Default.EsqlAsyncRequest, ct).ConfigureAwait(false)
		);
		return (postData, endpoint);
	}

	private EsqlRequest BuildRequest(string esql, EsqlParameters? parameters, EsqlQueryOptions? options)
	{
		var defaults = _settings.Defaults;
		return new EsqlRequest
		{
			Query = esql,
			Locale = options?.Locale ?? defaults.Locale,
			TimeZone = options?.TimeZone ?? defaults.TimeZone,
			Params = FormatParameters(parameters)
		};
	}

	private EsqlAsyncRequest BuildAsyncRequest(
		string esql,
		EsqlParameters? parameters,
		EsqlQueryOptions? options,
		EsqlAsyncQueryOptions? asyncOptions)
	{
		var defaults = _settings.Defaults;
		return new EsqlAsyncRequest
		{
			Query = esql,
			Locale = options?.Locale ?? defaults.Locale,
			TimeZone = options?.TimeZone ?? defaults.TimeZone,
			Params = FormatParameters(parameters),
			WaitForCompletionTimeout = asyncOptions?.WaitForCompletionTimeout,
			KeepAlive = asyncOptions?.KeepAlive,
			KeepOnCompletion = asyncOptions?.KeepOnCompletion ?? false
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

	private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? FormatParameters(EsqlParameters? parameters)
	{
		if (parameters is null || !parameters.HasParameters)
			return null;

		return [.. parameters.Parameters.Select(kvp => new Dictionary<string, JsonElement> { [kvp.Key] = kvp.Value })];
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
