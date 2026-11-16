// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class PooledBufferWriterTests
{
	[Test]
	public void Write_BeyondCapacity_GrowsAndKeepsWrittenBytes()
	{
		using var writer = new PooledBufferWriter(16);
		writer.Write("abcd"u8);

		writer.Write("efghijklmnopqrstuvwxyz"u8);

		writer.WrittenCount.Should().Be(26);
		writer.WrittenSpan.ToArray().Should().Equal("abcdefghijklmnopqrstuvwxyz"u8.ToArray());
	}

	[Test]
	public void ResetWrittenCount_KeepsCapacityAndClearsLength()
	{
		using var writer = new PooledBufferWriter(16);
		writer.Write("abcdefgh"u8);

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
	public void Write_AfterDispose_Throws()
	{
		var writer = new PooledBufferWriter(16);
		writer.Dispose();

		Action act = () => writer.Write(1);

		act.Should().Throw<ObjectDisposedException>();
	}
}
