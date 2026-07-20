// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.QueryModel;

public class EsqlAsyncQueryOptionsTests
{
	[Test]
	public void KeepAlive_Negative_ThrowsArgumentOutOfRange()
	{
		var act = () => new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromSeconds(-1) };

		_ = act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("KeepAlive");
	}

	[Test]
	public void WaitForCompletionTimeout_Negative_ThrowsArgumentOutOfRange()
	{
		var act = () => new EsqlAsyncQueryOptions { WaitForCompletionTimeout = TimeSpan.FromTicks(-1) };

		_ = act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("WaitForCompletionTimeout");
	}

	[Test]
	public void KeepAlive_ZeroAndPositive_Accepted()
	{
		var zero = new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.Zero };
		var positive = new EsqlAsyncQueryOptions { KeepAlive = TimeSpan.FromMinutes(5) };

		_ = zero.KeepAlive.Should().Be(TimeSpan.Zero);
		_ = positive.KeepAlive.Should().Be(TimeSpan.FromMinutes(5));
	}

	[Test]
	public void Defaults_StayNull()
	{
		var options = new EsqlAsyncQueryOptions();

		_ = options.KeepAlive.Should().BeNull();
		_ = options.WaitForCompletionTimeout.Should().BeNull();
	}
}
