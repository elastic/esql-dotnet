// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>Options for async ES|QL query execution.</summary>
public record EsqlAsyncQueryOptions
{
	/// <summary>How long to wait before returning async ID. Default: 1s.</summary>
	public TimeSpan? WaitForCompletionTimeout { get; init; }

	/// <summary>How long to keep results. Default: 5d.</summary>
	public TimeSpan? KeepAlive { get; init; }

	/// <summary>Keep results even if completed within timeout.</summary>
	public bool KeepOnCompletion { get; init; }
}
