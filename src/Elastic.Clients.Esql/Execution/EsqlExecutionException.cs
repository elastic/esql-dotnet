// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Clients.Esql.Execution;

/// <summary>Exception thrown when ES|QL query execution fails.</summary>
public class EsqlExecutionException(
	string message,
	ApiCallDetails? apiCallDetails,
	ElasticsearchServerError? serverError
) : Exception(message)
{
	/// <summary>The response body from Elasticsearch (stringified form of <see cref="ServerError"/> when available).</summary>
	public string? ResponseBody { get; } = serverError?.ToString();

	/// <summary>The HTTP status code, shorthand for <c>ApiCallDetails?.HttpStatusCode</c>.</summary>
	public int? StatusCode { get; } = apiCallDetails?.HttpStatusCode;

	/// <summary>The structured Elasticsearch server error, when one was returned and parsed.</summary>
	public ElasticsearchServerError? ServerError { get; } = serverError;

	/// <summary>Transport-level details about the failed call: URL, headers, timing, original exception, etc.</summary>
	public ApiCallDetails? ApiCallDetails { get; } = apiCallDetails;
}
