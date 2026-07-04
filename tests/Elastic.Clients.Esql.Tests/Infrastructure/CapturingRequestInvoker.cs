// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Transport;

namespace Elastic.Clients.Esql.Tests;

/// <summary>
/// An <see cref="IRequestInvoker"/> that performs no IO. Captures the endpoint, the bound
/// configuration, and the serialized request body of the most recent request and serves a
/// canned response. The response content type echoes the request Accept header so that
/// product-level response validation passes for any requested format.
/// </summary>
internal sealed class CapturingRequestInvoker(byte[] responseBody, int statusCode = 200) : IRequestInvoker
{
	private readonly InMemoryRequestInvoker _inner = new(responseBody, statusCode);

	public Endpoint? LastEndpoint { get; private set; }
	public BoundConfiguration? LastBoundConfiguration { get; private set; }
	public string? LastRequestBody { get; private set; }

	public ResponseFactory ResponseFactory => _inner.ResponseFactory;

	public TResponse Request<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
		where TResponse : TransportResponse, new()
	{
		Capture(endpoint, boundConfiguration, postData);
		return _inner.BuildResponse<TResponse>(endpoint, boundConfiguration, postData, contentType: boundConfiguration.Accept);
	}

	public Task<TResponse> RequestAsync<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData, CancellationToken cancellationToken)
		where TResponse : TransportResponse, new()
	{
		Capture(endpoint, boundConfiguration, postData);
		return _inner.BuildResponseAsync<TResponse>(endpoint, boundConfiguration, postData, cancellationToken, contentType: boundConfiguration.Accept);
	}

	void IDisposable.Dispose() { }

	private void Capture(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData? postData)
	{
		LastEndpoint = endpoint;
		LastBoundConfiguration = boundConfiguration;

		if (postData is null)
		{
			LastRequestBody = null;
			return;
		}

		using var buffer = new MemoryStream();
		postData.Write(buffer, boundConfiguration.ConnectionSettings, disableDirectStreaming: false);
		LastRequestBody = Encoding.UTF8.GetString(buffer.ToArray());
	}
}
