// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET10_0_OR_GREATER
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderPipeTests
{
	private const string ValuesFirstRowsJson =
		"""{"values":[["first",1],["second",2]],"columns":[{"name":"value","type":"keyword"},{"name":"count","type":"integer"}]}""";

	private const string ValuesFirstScalarJson =
		"""{"values":[[10],[20]],"columns":[{"name":"count","type":"integer"}]}""";

	[Test]
	public async Task ReadRowsAsync_Pipe_ValuesFirst_DrainsWithoutContractViolation()
	{
		var pipeReader = await CreateCompletedPipe(ValuesFirstRowsJson);
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(pipeReader);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		_ = rows.Should().HaveCount(2);
	}

	[Test]
	public async Task ReadScalarAsync_Pipe_ValuesFirst_ReturnsFirstValueAndRowCount()
	{
		var pipeReader = await CreateCompletedPipe(ValuesFirstScalarJson);
		var reader = CreateReader();

		var result = await reader.ReadScalarAsync<int>(pipeReader);

		_ = result.Value.Should().Be(10);
		_ = result.RowCount.Should().Be(2);
	}

	[Test]
	public async Task ReadRowsAsync_Pipe_ValuesFirstChunked_DrainsWithoutContractViolation()
	{
		var pipe = new Pipe();
		var bytes = Encoding.UTF8.GetBytes(ValuesFirstRowsJson);
		await pipe.Writer.WriteAsync(bytes.AsMemory(0, 10));
		_ = await pipe.Writer.FlushAsync();
		await pipe.Writer.WriteAsync(bytes.AsMemory(10));
		await pipe.Writer.CompleteAsync();
		var reader = CreateReader();

		await using var response = await reader.ReadRowsAsync<ScalarStringModel>(pipe.Reader);
		var rows = new List<ScalarStringModel>();
		await foreach (var row in response.Rows)
			rows.Add(row);

		_ = rows.Should().HaveCount(2);
	}

	private static async Task<PipeReader> CreateCompletedPipe(string json)
	{
		var pipe = new Pipe();
		await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(json));
		await pipe.Writer.CompleteAsync();
		return pipe.Reader;
	}

	private static EsqlResponseReader CreateReader()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				MaterializationTestJsonContext.Default,
				EsqlTestMappingContext.Default
			)
		};

		var metadata = new JsonMetadataManager(options);
		return new EsqlResponseReader(metadata);
	}
}
#endif
