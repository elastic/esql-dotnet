// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql;
using Elastic.Esql.Execution;

namespace Elastic.Clients.Esql.Tests.Execution;

public class EsqlTransportExecutorTimeSpanTests
{
	private static string SubmitAndCaptureBody(EsqlAsyncQueryOptions asyncOptions)
	{
		var (executor, invoker) = TestExecutorFactory.Create();
		using var response = executor.SubmitAsyncQuery(new EsqlExecutionRequest { Esql = "Q", AsyncOptions = asyncOptions });
		return invoker.LastRequestBody!;
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveWholeDays_FormatsAsDays()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromDays(5) });

		_ = body.Should().Contain("\"keep_alive\":\"5d\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveWholeHours_FormatsAsHours()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromHours(3) });

		_ = body.Should().Contain("\"keep_alive\":\"3h\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveNinetyMinutes_FormatsAsMinutes()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromMinutes(90) });

		_ = body.Should().Contain("\"keep_alive\":\"90m\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveWholeSeconds_FormatsAsSeconds()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromSeconds(2) });

		_ = body.Should().Contain("\"keep_alive\":\"2s\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveWholeMilliseconds_FormatsAsMilliseconds()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromMilliseconds(500) });

		_ = body.Should().Contain("\"keep_alive\":\"500ms\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepAliveMixedDuration_FallsToLargestWholeUnit()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromDays(1) + TimeSpan.FromHours(1) });

		_ = body.Should().Contain("\"keep_alive\":\"25h\"");
	}

	[Test]
	[Skip("FormatTimeSpan renders sub-millisecond TimeSpans as fractional milliseconds (0.5ms), but Elasticsearch rejects fractional time values. Expected-correct behavior is whole micros/nanos, e.g. 500micros for 5000 ticks. Product fix belongs to separate work.")]
	public void SubmitAsyncQuery_KeepAliveSubMillisecond_FormatsAsWholeMicros()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromTicks(5000) });

		_ = body.Should().Contain("\"keep_alive\":\"500micros\"");
	}

	[Test]
	public void SubmitAsyncQuery_WaitForCompletionTimeout_FormatsLikeKeepAlive()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { WaitForCompletionTimeout = TimeSpan.FromMinutes(90) });

		_ = body.Should().Contain("\"wait_for_completion_timeout\":\"90m\"");
	}

	[Test]
	public void SubmitAsyncQuery_KeepOnCompletionFalse_OmitsFieldFromBody()
	{
		var body = SubmitAndCaptureBody(new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromDays(5), KeepOnCompletion = false });

		_ = body.Should().NotContain("keep_on_completion");
	}
}
