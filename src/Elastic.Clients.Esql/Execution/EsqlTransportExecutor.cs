// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;
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
	private static readonly HeadersList AsyncHeaders = new(["X-Elasticsearch-Async-Id", "X-Elasticsearch-Async-Is-Running"]);
	private static readonly RequestConfiguration DefaultAsyncRequestConfig = new() { ResponseHeadersToParse = AsyncHeaders };

	public IEsqlResponse ExecuteQuery(EsqlExecutionRequest request)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var postData = BuildPostData(request.Esql, request.Parameters, typedOptions);
		var endpoint = BuildEndpoint(QueryEndpoint, typedOptions, request.Format);
		var requestConfig = ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format);
		var response = _settings.Transport.Request<ElasticsearchStreamResponse>(in endpoint, postData, null, requestConfig);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> ExecuteQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var postData = BuildPostData(request.Esql, request.Parameters, typedOptions);
		var endpoint = BuildEndpoint(QueryEndpoint, typedOptions, request.Format);
		var requestConfig = ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format);

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchPipeResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL query failed").ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchStreamResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse SubmitAsyncQuery(EsqlExecutionRequest request)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var (postData, endpoint) = BuildAsyncPostData(request.Esql, request.Parameters, typedOptions, request.AsyncOptions, request.Format);
		var requestConfig = EnsureAsyncHeaders(ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format));
		var response = _settings.Transport.Request<ElasticsearchStreamResponse>(in endpoint, postData, null, requestConfig);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> SubmitAsyncQueryAsync(EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var (postData, endpoint) = BuildAsyncPostData(request.Esql, request.Parameters, typedOptions, request.AsyncOptions, request.Format);
		var requestConfig = EnsureAsyncHeaders(ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format));

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchPipeResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "ES|QL async query failed").ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchStreamResponse>(in endpoint, postData, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "ES|QL async query failed");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public IEsqlResponse PollAsyncQuery(string queryId, EsqlExecutionRequest request)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var endpointPath = BuildAsyncQueryEndpoint(HttpMethod.GET, queryId, request.Format);
		var requestConfig = EnsureAsyncHeaders(ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format));
		var response = _settings.Transport.Request<ElasticsearchStreamResponse>(in endpointPath, null, null, requestConfig);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlResponse(response);
	}

	public async Task<IEsqlAsyncResponse> PollAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var endpointPath = BuildAsyncQueryEndpoint(HttpMethod.GET, queryId, request.Format);
		var requestConfig = EnsureAsyncHeaders(ApplyAcceptForFormat(typedOptions?.RequestConfiguration, request.Format));

#if NET10_0_OR_GREATER
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchPipeResponse>(in endpointPath, null, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		await ThrowIfErrorAsync(response, "Failed to get async query status").ConfigureAwait(false);
		return new TransportEsqlAsyncResponse(response);
#else
		var response = await _settings.Transport
			.RequestAsync<ElasticsearchStreamResponse>(in endpointPath, null, null, requestConfig, cancellationToken)
			.ConfigureAwait(false);
		ThrowIfError(response, "Failed to get async query status");
		return new TransportEsqlAsyncResponse(response);
#endif
	}

	public void DeleteAsyncQuery(string queryId, EsqlExecutionRequest request)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var endpointPath = BuildAsyncQueryEndpoint(HttpMethod.DELETE, queryId, format: null);
		using var response = _settings.Transport.Request<ElasticsearchStreamResponse>(in endpointPath, null, null, typedOptions?.RequestConfiguration);
		ThrowIfError(response, "Failed to delete async query");
	}

	public async Task DeleteAsyncQueryAsync(string queryId, EsqlExecutionRequest request, CancellationToken cancellationToken)
	{
		var typedOptions = ResolveOptions(request.ExecutorOptions);
		var endpointPath = BuildAsyncQueryEndpoint(HttpMethod.DELETE, queryId, format: null);
		using var response = await _settings.Transport
			.RequestAsync<ElasticsearchStreamResponse>(in endpointPath, null, null, typedOptions?.RequestConfiguration, cancellationToken)
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

	private EndpointPath BuildAsyncQueryEndpoint(HttpMethod method, string queryId, EsqlFormat? format)
	{
		if (string.IsNullOrWhiteSpace(queryId))
			throw new ArgumentException("Async query ID cannot be null or empty.", nameof(queryId));

		var basePath = $"/_query/async/{Uri.EscapeDataString(queryId)}";

		if (format is null)
			return new EndpointPath(method, basePath);

		var parameters = new DefaultRequestParameters();
		parameters.SetQueryString("format", format.Value.GetFormatName());
		var pathWithQuery = parameters.CreatePathWithQueryStrings(basePath, _settings.Transport.Configuration);
		return new EndpointPath(method, pathWithQuery);
	}

	private static void ThrowIfError(ElasticsearchStreamResponse response, string operation)
	{
		if (response.IsValidResponse)
			return;

		var apiCallDetails = response.ApiCallDetails;
		var serverError = response.ElasticsearchServerError;
		var message = serverError?.Error is { } error
			? $"{operation}: {error}"
			: $"{operation}: {apiCallDetails?.HttpStatusCode}";

		response.Dispose();
		throw new EsqlExecutionException(message, apiCallDetails, serverError);
	}

#if NET10_0_OR_GREATER
	private static async Task ThrowIfErrorAsync(ElasticsearchPipeResponse response, string operation)
	{
		if (response.IsValidResponse)
			return;

		var apiCallDetails = response.ApiCallDetails;
		var serverError = response.ElasticsearchServerError;
		var message = serverError?.Error is { } error
			? $"{operation}: {error}"
			: $"{operation}: {apiCallDetails?.HttpStatusCode}";

		await response.DisposeAsync().ConfigureAwait(false);
		throw new EsqlExecutionException(message, apiCallDetails, serverError);
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
		EsqlAsyncQueryOptions? asyncOptions,
		EsqlFormat? format)
	{
		var request = BuildAsyncRequest(esql, parameters, options, asyncOptions);
		var endpoint = BuildEndpoint(AsyncQueryEndpoint, options, format);
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
			WaitForCompletionTimeout = asyncOptions?.WaitForCompletionTimeout is { } wfc ? FormatTimeSpan(wfc) : null,
			KeepAlive = asyncOptions?.KeepAlive is { } ka ? FormatTimeSpan(ka) : null,
			KeepOnCompletion = asyncOptions?.KeepOnCompletion ?? false
		};
	}

	private EndpointPath BuildEndpoint(EndpointPath basePath, EsqlQueryOptions? options, EsqlFormat? format)
	{
		var hasOptions = options?.AllowPartialResults is not null || options?.DropNullColumns is not null;
		if (!hasOptions && format is null)
			return basePath;

		var parameters = new DefaultRequestParameters();

		if (options?.AllowPartialResults is { } allowPartial)
			parameters.SetQueryString("allow_partial_results", allowPartial);

		if (options?.DropNullColumns is { } dropNull)
			parameters.SetQueryString("drop_null_columns", dropNull);

		if (format is { } fmt)
			parameters.SetQueryString("format", fmt.GetFormatName());

		var pathWithQuery = parameters.CreatePathWithQueryStrings(basePath.PathAndQuery, _settings.Transport.Configuration);
		return new EndpointPath(basePath.Method, pathWithQuery);
	}

	private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? FormatParameters(EsqlParameters? parameters)
	{
		if (parameters is null || !parameters.HasParameters)
			return null;

		return [.. parameters.Parameters.Select(kvp => new Dictionary<string, JsonElement> { [kvp.Key] = kvp.Value })];
	}

	private static string FormatTimeSpan(TimeSpan ts)
	{
		if (ts.Ticks % TimeSpan.TicksPerDay == 0)
			return $"{ts.Ticks / TimeSpan.TicksPerDay}d";
		if (ts.Ticks % TimeSpan.TicksPerHour == 0)
			return $"{ts.Ticks / TimeSpan.TicksPerHour}h";
		if (ts.Ticks % TimeSpan.TicksPerMinute == 0)
			return $"{ts.Ticks / TimeSpan.TicksPerMinute}m";
		if (ts.Ticks % TimeSpan.TicksPerSecond == 0)
			return $"{ts.Ticks / TimeSpan.TicksPerSecond}s";
		if (ts.Ticks % TimeSpan.TicksPerMillisecond == 0)
			return $"{ts.Ticks / TimeSpan.TicksPerMillisecond}ms";

		return $"{ts.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)}ms";
	}

	private static IRequestConfiguration? ApplyAcceptForFormat(IRequestConfiguration? userConfig, EsqlFormat? format)
	{
		if (format is null)
			return userConfig;

		var mediaType = format.Value.GetMediaType();

		if (userConfig is null)
			return new RequestConfiguration { Accept = mediaType };

		if (!string.IsNullOrEmpty(userConfig.Accept))
			return userConfig;

		return new RequestConfiguration(userConfig) { Accept = mediaType };
	}

	private static IRequestConfiguration EnsureAsyncHeaders(IRequestConfiguration? userConfig)
	{
		if (userConfig is null)
			return DefaultAsyncRequestConfig;

		var existing = userConfig.ResponseHeadersToParse;
		if (existing is not null && ContainsAllAsyncHeaders(existing.Value))
			return userConfig;

		return new RequestConfiguration(userConfig)
		{
			ResponseHeadersToParse = existing is not null
				? new HeadersList(existing, AsyncHeaders)
				: AsyncHeaders
		};

		static bool ContainsAllAsyncHeaders(HeadersList headers)
		{
			foreach (var required in AsyncHeaders)
			{
				if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
					return false;
			}
			return true;
		}
	}
}

/// <summary>Wraps an <see cref="ElasticsearchStreamResponse"/> as an <see cref="IEsqlResponse"/>.</summary>
internal sealed class TransportEsqlResponse(ElasticsearchStreamResponse response) : IEsqlResponse
{
	public Stream Body => response.Body;

	public bool TryGetHeader(string name, out IEnumerable<string> values)
	{
		if (response.ApiCallDetails.TryGetHeader(name, out var found))
		{
			values = found;
			return true;
		}

		values = [];
		return false;
	}

	public void Dispose() => response.Dispose();
}

#if NET10_0_OR_GREATER
/// <summary>Wraps an <see cref="ElasticsearchPipeResponse"/> as an <see cref="IEsqlAsyncResponse"/>, using its native <see cref="PipeReader"/>.</summary>
internal sealed class TransportEsqlAsyncResponse(ElasticsearchPipeResponse response) : IEsqlAsyncResponse
{
	public PipeReader Body => response.Body;

	public bool TryGetHeader(string name, out IEnumerable<string> values)
	{
		if (response.ApiCallDetails.TryGetHeader(name, out var found))
		{
			values = found;
			return true;
		}

		values = [];
		return false;
	}

	public async ValueTask DisposeAsync() =>
		await response.DisposeAsync().ConfigureAwait(false);
}
#else
/// <summary>Wraps an <see cref="ElasticsearchStreamResponse"/> as an <see cref="IEsqlAsyncResponse"/>.</summary>
internal sealed class TransportEsqlAsyncResponse(ElasticsearchStreamResponse response) : IEsqlAsyncResponse
{
	public Stream Body => response.Body;

	public bool TryGetHeader(string name, out IEnumerable<string> values)
	{
		if (response.ApiCallDetails.TryGetHeader(name, out var found))
		{
			values = found;
			return true;
		}

		values = [];
		return false;
	}

	public ValueTask DisposeAsync()
	{
		response.Dispose();
		return default;
	}
}
#endif
