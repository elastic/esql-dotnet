// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql;

/// <summary>Options for async ES|QL query submission behavior.</summary>
public sealed record EsqlAsyncQueryOptions
{
	// IDE0032 suggests an auto-property, but the field is written from a guarded init accessor.
#pragma warning disable IDE0032
	private readonly TimeSpan? _waitForCompletionTimeout;
	private readonly TimeSpan? _keepAlive;
#pragma warning restore IDE0032

	/// <summary>How long to wait before returning async ID. Default: 1s.</summary>
	public TimeSpan? WaitForCompletionTimeout
	{
		get => _waitForCompletionTimeout;
		init
		{
			// Elasticsearch rejects negative durations; fail at construction instead of at the API.
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(WaitForCompletionTimeout), value, "Duration must not be negative.");
			_waitForCompletionTimeout = value;
		}
	}

	/// <summary>How long to keep results. Default: 5d.</summary>
	public TimeSpan? KeepAlive
	{
		get => _keepAlive;
		init
		{
			// Elasticsearch rejects negative durations; fail at construction instead of at the API.
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(KeepAlive), value, "Duration must not be negative.");
			_keepAlive = value;
		}
	}

	/// <summary>Keep results even if completed within timeout.</summary>
	public bool KeepOnCompletion { get; init; }
}
