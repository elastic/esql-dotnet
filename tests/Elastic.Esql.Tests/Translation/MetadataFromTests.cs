// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class MetadataFromTests : EsqlTestBase
{
	[Test]
	public void From_NoMetadata_DoesNotEmitMetadataDirective()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*")
			.ToString();

		_ = esql.Should().Be("FROM logs-*");
	}

	[Test]
	public void From_SingleMetadataField_EmitsMetadataDirective()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Score)
			.ToString();

		_ = esql.Should().Be("FROM logs-* METADATA _score");
	}

	[Test]
	public void From_MultipleMetadataFields_EmitsCanonicalOrder()
	{
		var esql = CreateQuery<LogEntry>()
			.From("books", MetadataField.Score | MetadataField.Id | MetadataField.Index)
			.ToString();

		_ = esql.Should().Be("FROM books METADATA _id, _index, _score");
	}

	[Test]
	public void From_AllMetadata_EmitsAllEightFields()
	{
		var esql = CreateQuery<LogEntry>()
			.From("idx", MetadataField.All)
			.ToString();

		_ = esql.Should().Be("FROM idx METADATA _id, _ignored, _index, _index_mode, _score, _size, _source, _version");
	}

	[Test]
	public void From_WithMetadata_AutoRetainsInKeepAfterSelect()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Score | MetadataField.Id)
			.Select(l => new { l.Message })
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-* METADATA _id, _score
			| KEEP message, _id, _score
			""".NativeLineEndings());
	}

	[Test]
	public void From_WithMetadata_DroppedFromKeep_AfterStats()
	{
		// STATS clears active metadata (per ES|QL semantics). _score appears only in METADATA on FROM,
		// not in any subsequent KEEP.
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Score)
			.GroupBy(l => l.Level)
			.Select(g => new { Level = g.Key, Count = g.Count() })
			.ToString();

		_ = esql.Should().NotContain("KEEP");
		_ = esql.Should().Contain("METADATA _score");
	}
}
