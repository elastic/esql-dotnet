// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.MappingParity;

/// <summary>
/// Verifies that context-based (source-generated) and reflection-based metadata resolution
/// materialize identical objects from the same ES|QL response.
/// </summary>
public class ContextVsReflectionMaterializationTests
{
	private static readonly JsonMetadataManager ContextMetadata = new(
		new JsonSerializerOptions
		{
			TypeInfoResolver = EsqlTestMappingContext.Default,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

	private static readonly JsonMetadataManager ReflectionMetadata = new(
		new JsonSerializerOptions
		{
			TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

	private static List<T> ReadRows<T>(string json, JsonMetadataManager metadata)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var reader = new EsqlResponseReader(metadata);
		using var results = reader.ReadRows<T>(stream);
		return results.Rows.ToList();
	}

	[Test]
	public void Materialize_BasicTypes_ProducesIdenticalObjects()
	{
		var json = """
			{
			  "columns": [
			    { "name": "message", "type": "keyword" },
			    { "name": "statusCode", "type": "integer" },
			    { "name": "duration", "type": "double" },
			    { "name": "isError", "type": "boolean" }
			  ],
			  "values": [
			    ["test message", 200, 1.5, false]
			  ]
			}
			""";

		var ctxResult = ReadRows<LogEntry>(json, ContextMetadata);
		var reflResult = ReadRows<LogEntry>(json, ReflectionMetadata);

		_ = ctxResult.Should().HaveCount(1);
		_ = reflResult.Should().HaveCount(1);
		_ = ctxResult[0].Message.Should().Be(reflResult[0].Message);
		_ = ctxResult[0].StatusCode.Should().Be(reflResult[0].StatusCode);
		_ = ctxResult[0].Duration.Should().Be(reflResult[0].Duration);
		_ = ctxResult[0].IsError.Should().Be(reflResult[0].IsError);
		_ = ctxResult[0].Message.Should().Be("test message");
		_ = ctxResult[0].StatusCode.Should().Be(200);
	}

	[Test]
	public void Materialize_Enum_ProducesIdenticalObjects()
	{
		var json = """
			{
			  "columns": [
			    { "name": "level", "type": "keyword" },
			    { "name": "message", "type": "keyword" }
			  ],
			  "values": [
			    ["Error", "test"]
			  ]
			}
			""";

		var ctxResult = ReadRows<EventDocument>(json, ContextMetadata);
		var reflResult = ReadRows<EventDocument>(json, ReflectionMetadata);

		_ = ctxResult[0].Level.Should().Be(reflResult[0].Level);
		_ = ctxResult[0].Level.Should().Be(LogLevel.Error);
	}

	[Test]
	public void Materialize_Nullable_ProducesIdenticalObjects()
	{
		var json = """
			{
			  "columns": [
			    { "name": "name", "type": "keyword" },
			    { "name": "value", "type": "double" },
			    { "name": "count", "type": "integer" }
			  ],
			  "values": [
			    ["cpu", 95.5, null]
			  ]
			}
			""";

		var ctxResult = ReadRows<MetricDocument>(json, ContextMetadata);
		var reflResult = ReadRows<MetricDocument>(json, ReflectionMetadata);

		_ = ctxResult[0].Name.Should().Be(reflResult[0].Name);
		_ = ctxResult[0].Value.Should().Be(reflResult[0].Value);
		_ = ctxResult[0].Count.Should().Be(reflResult[0].Count);
		_ = ctxResult[0].Count.Should().BeNull();
	}

	[Test]
	public void Materialize_Guid_ProducesIdenticalObjects()
	{
		var json = """
			{
			  "columns": [
			    { "name": "eventId", "type": "keyword" },
			    { "name": "message", "type": "keyword" },
			    { "name": "level", "type": "keyword" }
			  ],
			  "values": [
			    ["d3b07384-d9a0-4e9a-8e1a-3b1c4c5d6e7f", "test", "Info"]
			  ]
			}
			""";

		var ctxResult = ReadRows<EventDocument>(json, ContextMetadata);
		var reflResult = ReadRows<EventDocument>(json, ReflectionMetadata);

		_ = ctxResult[0].EventId.Should().Be(reflResult[0].EventId);
		_ = ctxResult[0].EventId.Should().Be(Guid.Parse("d3b07384-d9a0-4e9a-8e1a-3b1c4c5d6e7f"));
	}

	[Test]
	public void Materialize_IgnoredColumn_BothPathsLeaveInternalIdDefault()
	{
		// LogEntry.InternalId carries [JsonIgnore]; the response column must not populate it.
		var json = """
			{
			  "columns": [
			    { "name": "message", "type": "keyword" },
			    { "name": "internalId", "type": "keyword" }
			  ],
			  "values": [
			    ["hello", "secret-123"]
			  ]
			}
			""";

		var ctxResult = ReadRows<LogEntry>(json, ContextMetadata);
		var reflResult = ReadRows<LogEntry>(json, ReflectionMetadata);

		_ = ctxResult[0].InternalId.Should().BeEmpty();
		_ = reflResult[0].InternalId.Should().BeEmpty();
	}

	[Test]
	public void Materialize_RegularColumn_BothPathsPopulateMessage()
	{
		var json = """
			{
			  "columns": [
			    { "name": "message", "type": "keyword" }
			  ],
			  "values": [
			    ["hello"]
			  ]
			}
			""";

		var ctxResult = ReadRows<LogEntry>(json, ContextMetadata);
		var reflResult = ReadRows<LogEntry>(json, ReflectionMetadata);

		_ = ctxResult[0].Message.Should().Be("hello");
		_ = reflResult[0].Message.Should().Be("hello");
	}
}
