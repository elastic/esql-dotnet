// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation;

public class EsqlMetadataMarkerTests : EsqlTestBase
{
	[Test]
	public void EsqlMetadataScore_InOrderBy_EmitsScoreColumn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Score)
			.OrderByDescending(_ => EsqlMetadata.Score)
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-* METADATA _score
			| SORT _score DESC
			""".NativeLineEndings());
	}

	[Test]
	public void EsqlMetadataId_InWhere_EmitsIdColumn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("logs-*", MetadataField.Id)
			.Where(l => EsqlMetadata.Id == "doc1")
			.ToString();

		_ = esql.Should().Be(
			"""
			FROM logs-* METADATA _id
			| WHERE _id == "doc1"
			""".NativeLineEndings());
	}

	[Test]
	public void EsqlMetadata_NotRequested_ThrowsHelpfulError()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderByDescending(_ => EsqlMetadata.Score)
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>()
			.WithMessage("*EsqlMetadata.Score*not requested*");
	}

	[Test]
	public void EsqlMetadataScore_InProjection_RenamesToTargetColumn()
	{
		var esql = CreateQuery<LogEntry>()
			.From("books", MetadataField.Id | MetadataField.Score)
			.Select(b => new { Id = EsqlMetadata.Id, MyScore = EsqlMetadata.Score, b.Message })
			.ToString();

		// _id and _score are consumed by the projection (renamed); auto-retain skips them.
		// Result column names go through the configured camelCase naming policy.
		_ = esql.Should().Be(
			"""
			FROM books METADATA _id, _score
			| RENAME _id AS id, _score AS myScore
			| KEEP message, id, myScore
			""".NativeLineEndings());
	}

	[Test]
	public void EsqlMetadataFork_BeforeFork_ThrowsHelpfulError()
	{
		var act = () => CreateQuery<LogEntry>()
			.From("logs-*")
			.OrderBy(_ => EsqlMetadata.Fork)
			.ToString();

		_ = act.Should().Throw<InvalidOperationException>()
			.WithMessage("*EsqlMetadata.Fork*'Fork' command*");
	}
}
