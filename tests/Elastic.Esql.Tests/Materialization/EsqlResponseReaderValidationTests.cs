// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class EsqlResponseReaderValidationTests
{
	[Test]
	public void ReadRows_WhenRowHasMoreValuesThanColumns_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "a", "type": "integer" },
			    { "name": "b", "type": "integer" }
			  ],
			  "values": [
			    [1, 2, 3]
			  ]
			}
			""";

		var act = () => ReadRows<TestRow>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*more values*");
	}

	[Test]
	public void ReadRows_WhenRowHasFewerValuesThanColumns_ThrowsJsonException()
	{
		var json = """
			{
			  "columns": [
			    { "name": "a", "type": "integer" },
			    { "name": "b", "type": "integer" }
			  ],
			  "values": [
			    [1]
			  ]
			}
			""";

		var act = () => ReadRows<TestRow>(json);

		_ = act.Should()
			.Throw<JsonException>()
			.WithMessage("*fewer values*");
	}

	private static void ReadRows<T>(string json)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var metadata = new JsonMetadataManager(new JsonSerializerOptions(JsonSerializerDefaults.Web));
		var reader = new EsqlResponseReader(metadata);
		_ = reader.ReadRows<T>(stream).ToList();
	}

	private sealed class TestRow
	{
		public int A { get; init; }

		public int B { get; init; }
	}
}
