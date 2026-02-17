// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Execution;

/// <summary>Exception thrown when ES|QL query execution fails.</summary>
public class EsqlExecutionException : Exception
{
	/// <summary>The response body from Elasticsearch.</summary>
	public string? ResponseBody { get; }

	/// <summary>The HTTP status code.</summary>
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
