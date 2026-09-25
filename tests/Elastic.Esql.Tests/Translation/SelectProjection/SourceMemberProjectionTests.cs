// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Tests.Translation.SelectProjection;

/// <summary>
/// A member of a projection declared as read from <c>_source</c> with
/// <c>EsqlMetadata.SourceAs(o.Member)</c>: the document is kept, and the member contributes
/// no column of its own.
/// </summary>
public class SourceMemberProjectionTests : EsqlTestBase
{
	[Test]
	public void Select_AMemberFromTheDocument_KeepsSource()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders", MetadataField.Source)
			.Select(o => new SourcedOrderDto { Reference = o.Reference, Total = o.Total, Lines = EsqlMetadata.SourceAs(o.Lines) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | KEEP reference, total, _source
            """.NativeLineEndings());
	}

	[Test]
	public void Select_TwoMembersFromTheDocument_KeepSourceOnce()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders", MetadataField.Source)
			.Select(o => new SourcedOrderDto
			{
				Reference = o.Reference,
				Lines = EsqlMetadata.SourceAs(o.Lines),
				Returns = EsqlMetadata.SourceAs(o.Returns)
			})
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | KEEP reference, _source
            """.NativeLineEndings());
	}

	[Test]
	public void Select_AMemberFromTheDocumentIntoAnAnonymousType_KeepsSource()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders", MetadataField.Source)
			.Select(o => new { o.Reference, Lines = EsqlMetadata.SourceAs(o.Lines) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | KEEP reference, _source
            """.NativeLineEndings());
	}

	[Test]
	public void Select_SourceAsOfSomethingOtherThanAMember_ThrowsNotSupported()
	{
		var query = CreateQuery<SourcedOrder>()
			.From("orders", MetadataField.Source)
			.Select(o => new { Lines = EsqlMetadata.SourceAs(new List<SourcedLine>()) });

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*takes a member of the document*");
	}

	[Test]
	public void Select_AMemberFromTheDocumentWithoutSourceRequested_RequestsIt()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders")
			.Where(o => o.Total > 10)
			.Select(o => new SourcedOrderDto { Reference = o.Reference, Lines = EsqlMetadata.SourceAs(o.Lines) })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | WHERE total > 10.0
            | KEEP reference, _source
            """.NativeLineEndings());
	}

	[Test]
	public void Select_AMemberFromTheDocumentWithOtherMetadataRequested_AddsSource()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders", MetadataField.Id)
			.Select(o => new { EsqlMetadata.Id, Lines = EsqlMetadata.SourceAs(o.Lines) })
			.ToString();

		_ = esql.Should().Contain("FROM orders METADATA _id, _source");
	}

	[Test]
	public void Select_TheWholeRowWithoutSourceRequested_RequestsIt()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders")
			.AsDocuments()
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | KEEP _source
            """.NativeLineEndings());
	}

	[Test]
	public void Select_TheDocumentIntoAMemberWithoutSourceRequested_RequestsIt()
	{
		var esql = CreateQuery<SourcedOrder>()
			.From("orders")
			.Select(o => new { o.Reference, Document = EsqlMetadata.SourceAs<SourcedOrder>() })
			.ToString();

		_ = esql.Should().Be(
			"""
            FROM orders METADATA _source
            | RENAME _source AS document
            | KEEP reference, document
            """.NativeLineEndings());
	}

	[Test]
	public void Select_AMemberFromTheDocumentAfterAKeep_ThrowsNotSupported()
	{
		// the KEEP has dropped the document from the rows by the time the projection reads it
		var query = CreateQuery<SourcedOrder>()
			.From("orders")
			.Keep("reference", "lines")
			.Select(o => new { Lines = EsqlMetadata.SourceAs(o.Lines) });

		var act = () => query.ToString();

		_ = act.Should().Throw<NotSupportedException>().WithMessage("*no longer carry after KEEP*MetadataField.Source*");
	}
}
