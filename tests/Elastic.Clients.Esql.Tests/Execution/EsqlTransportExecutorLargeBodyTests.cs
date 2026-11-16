// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.Buffers;
using System.Text.Json;
using Elastic.Clients.Esql.Tests.Infrastructure;
using Elastic.Esql.Extensions;

namespace Elastic.Clients.Esql.Tests.Execution;

/// <summary>
/// Verifies that the net10 async path materializes all rows from a large response body.
/// On net10 the executor wraps the stream in a 64 KB pipe, so this guards against regressions
/// where the larger segment size causes rows near a buffer boundary to be dropped or mis-parsed.
/// </summary>
public class EsqlTransportExecutorLargeBodyTests
{
	[Test]
	public async Task ExecuteQueryAsync_LargeResponseBody_EnumeratesAllRows()
	{
		const int rowCount = 2000;
		var payload = BuildPayload(rowCount);

		var invoker = new CapturingRequestInvoker(payload);
		var settings = TestExecutorFactory.CreateSettings(invoker);
		using var client = new EsqlClient(settings);
		var count = 0;

		await foreach (var _ in client.QueryAsync<LargeBodyRow>(q => q.From("test")))
			count++;

		_ = count.Should().Be(rowCount);
	}

	// Builds a JSON ES|QL response with `rowCount` rows.  Each name value is padded to push
	// the total payload above 200 KB so that reads cross multiple 64 KB pipe segments.
	private static byte[] BuildPayload(int rowCount)
	{
		const string padding = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		var buffer = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(buffer);

		writer.WriteStartObject();
		writer.WritePropertyName("columns");
		writer.WriteStartArray();
		writer.WriteStartObject();
		writer.WriteString("name", "name");
		writer.WriteString("type", "keyword");
		writer.WriteEndObject();
		writer.WriteEndArray();
		writer.WritePropertyName("values");
		writer.WriteStartArray();

		for (var i = 0; i < rowCount; i++)
		{
			writer.WriteStartArray();
			writer.WriteStringValue($"doc-{i:D4}-{padding}");
			writer.WriteEndArray();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();

		return buffer.WrittenSpan.ToArray();
	}

	private sealed class LargeBodyRow
	{
		public string Name { get; set; } = string.Empty;
	}
}
#endif
