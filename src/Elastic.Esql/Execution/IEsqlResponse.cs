// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO.Pipelines;

namespace Elastic.Esql.Execution;

/// <summary>Owns a synchronous ES|QL response stream. Disposing releases the underlying transport resource.</summary>
public interface IEsqlResponse : IDisposable
{
	/// <summary>The response body as a <see cref="Stream"/>.</summary>
	Stream Body { get; }
}

/// <summary>Owns an asynchronous ES|QL response pipe. Disposing releases the underlying transport resource.</summary>
public interface IEsqlAsyncResponse : IAsyncDisposable
{
	/// <summary>The response body as a <see cref="PipeReader"/>.</summary>
	PipeReader Body { get; }
}
