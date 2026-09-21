// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Execution;

public class OwnedStreamDisposalTests
{
	[Test]
	public async Task DisposeAsync_DisposesResponseOnce()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var stream = new OwnedAsyncResponseStream(response);

		await stream.DisposeAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public async Task Dispose_ThenDisposeAsync_DisposesResponseOnce()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var stream = new OwnedAsyncResponseStream(response);

		stream.Dispose();
		await stream.DisposeAsync();

		response.DisposeCount.Should().Be(1);
	}

	[Test]
	public void Dispose_Twice_DisposesResponseOnce()
	{
		var response = new CountingAsyncResponse("data"u8.ToArray());
		var stream = new OwnedAsyncResponseStream(response);

		stream.Dispose();
		stream.Dispose();

		response.DisposeCount.Should().Be(1);
	}
}
