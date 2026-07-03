// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderTruncationTests
{
	private const string TruncatedMidRowJson =
		"""{"columns":[{"name":"value","type":"keyword"},{"name":"count","type":"integer"}],"values":[["first",1],["seco""";

	private const string TruncatedInsideColumnsJson =
		"""{"columns":[{"name":"value","type":"keyw""";

	private const string TruncatedScalarJson =
		"""{"columns":[{"name":"count","type":"integer"}],"values":[[10],[2""";

	[Test]
	public async Task ReadRows_Stream_TruncatedMidRow_ThrowsJsonException()
	{
		var run = Task.Run(() =>
		{
			using var stream = CreateStream(TruncatedMidRowJson);
			var reader = CreateReader();

			var act = () => reader.ReadRows<ScalarStringModel>(stream).Rows.ToList();

			act.Should().Throw<JsonException>();
		});

		await AssertCompletes(run);
	}

	[Test]
	public async Task ReadRowsAsync_Stream_TruncatedMidRow_ThrowsJsonException()
	{
		var run = Task.Run(async () =>
		{
			using var stream = CreateStream(TruncatedMidRowJson);
			var reader = CreateReader();

			var act = async () =>
			{
				await using var response = await reader.ReadRowsAsync<ScalarStringModel>(stream);
				await foreach (var _ in response.Rows)
				{
				}
			};

			await act.Should().ThrowAsync<JsonException>();
		});

		await AssertCompletes(run);
	}

	[Test]
	public async Task ReadRows_Stream_TruncatedInsideColumns_ThrowsJsonException()
	{
		var run = Task.Run(() =>
		{
			using var stream = CreateStream(TruncatedInsideColumnsJson);
			var reader = CreateReader();

			var act = () => reader.ReadRows<ScalarStringModel>(stream).Rows.ToList();

			act.Should().Throw<JsonException>();
		});

		await AssertCompletes(run);
	}

	[Test]
	public async Task ReadScalarAsync_Stream_TruncatedMidValues_ThrowsJsonException()
	{
		var run = Task.Run(async () =>
		{
			using var stream = CreateStream(TruncatedScalarJson);
			var reader = CreateReader();

			var act = async () => await reader.ReadScalarAsync<int>(stream);

			await act.Should().ThrowAsync<JsonException>();
		});

		await AssertCompletes(run);
	}

	/// <summary>
	/// Awaits the assertion task with a deadline so a regression back to the EOF busy loop fails
	/// the test quickly instead of hanging the whole suite.
	/// </summary>
	private static async Task AssertCompletes(Task run)
	{
		var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
		finished.Should().BeSameAs(run, "a truncated response must fail fast instead of spinning");
		await run;
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

	private static MemoryStream CreateStream(string json) => new(Encoding.UTF8.GetBytes(json));
}
