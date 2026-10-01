// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class PooledBufferWriterTests
{
	[Test]
	public void GetSpan_BeyondCapacity_GrowsAndKeepsWrittenBytes()
	{
		using var writer = new PooledBufferWriter(16);
		var first = writer.GetSpan(4);
		"abcd"u8.CopyTo(first);
		writer.Advance(4);

		var second = writer.GetSpan(1000);
		second[..3].Fill((byte)'x');
		writer.Advance(3);

		writer.WrittenCount.Should().Be(7);
		writer.WrittenSpan.ToArray().Should().Equal("abcdxxx"u8.ToArray());
	}

	[Test]
	public void ResetWrittenCount_KeepsCapacityAndClearsLength()
	{
		using var writer = new PooledBufferWriter(16);
		writer.GetSpan(8)[0] = 1;
		writer.Advance(8);

		writer.ResetWrittenCount();

		writer.WrittenCount.Should().Be(0);
		writer.WrittenSpan.Length.Should().Be(0);
	}

	[Test]
	public void Dispose_Twice_DoesNotThrow()
	{
		var writer = new PooledBufferWriter(16);
		writer.Dispose();

		var act = writer.Dispose;

		act.Should().NotThrow();
	}

	[Test]
	public void GetSpan_AfterDispose_Throws()
	{
		var writer = new PooledBufferWriter(16);
		writer.Dispose();

		Action act = () => writer.GetSpan(1);

		act.Should().Throw<ObjectDisposedException>();
	}
}
