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

	/// <summary>
	/// How long the submit request waits for completion before returning an async id. Default: 1s.
	/// Applied to the submit request only; later polls return the current state immediately and
	/// the client waits between polls.
	/// </summary>
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

	/// <summary>
	/// How long Elasticsearch keeps the results. Default: 5d. Sent with the submit request and
	/// re-sent on every poll, so retention is extended from the most recent poll rather than from
	/// submission.
	/// </summary>
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
