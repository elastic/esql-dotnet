// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Clients.Esql.Execution;

/// <summary>Request for async ES|QL query execution.</summary>
public class EsqlAsyncRequest : EsqlRequest
{
	/// <summary>How long to wait before returning async ID. Default: 1s.</summary>
	public TimeSpan? WaitForCompletionTimeout { get; set; }

	/// <summary>How long to keep results. Default: 5d.</summary>
	public TimeSpan? KeepAlive { get; set; }

	/// <summary>Keep results even if completed within timeout.</summary>
	public bool KeepOnCompletion { get; set; }
}
