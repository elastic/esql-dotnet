// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using System.Text.Json;
using Elastic.Esql.Core;
using Elastic.Esql.Materialization;
using Elastic.Esql.Tests.Execution;

namespace Elastic.Esql.Tests.Materialization;

/// <summary>
/// Members declared as read from <c>_source</c> take their value from the document, at their path,
/// rather than from a column: a list of objects arrives as the document holds it.
/// </summary>
public class SourceMemberMaterializationTests
{
	private static readonly SourceMember Lines = new("lines", ["lines"]);

	private static string Response(params string[] documents) =>
		"{\"columns\":[{\"name\":\"reference\",\"type\":\"keyword\"},{\"name\":\"_source\",\"type\":\"_source\"}],\"values\":["
		+ string.Join(",", documents.Select((document, i) => $"[\"A{i + 1}\",{document}]"))
		+ "]}";

	private static List<T> ReadRows<T>(string json, params SourceMember[] members)
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = EsqlTestMappingContext.Default };
		return new EsqlResponseReader(new JsonMetadataManager(options), members).ReadRows<T>(stream).Rows.ToList();
	}

	private static string Skus(IEnumerable<SourcedLine> lines) => string.Join(",", lines.Select(line => $"{line.Sku}x{line.Qty}"));

	[Test]
	public void ReadRows_AListOfObjectsFromTheDocument_KeepsEachObjectWhole()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("""{"lines":[{"sku":"a","qty":1},{"sku":"b","qty":2}]}"""), Lines);

		_ = rows.Should().ContainSingle();
		_ = rows[0].Reference.Should().Be("A1");
		_ = Skus(rows[0].Lines).Should().Be("ax1,bx2");
	}

	[Test]
	public void ReadRows_TwoMembersFromTheDocument_ReadItOnce()
	{
		var rows = ReadRows<SourcedOrderDto>(
			Response("""{"lines":[{"sku":"a","qty":1}],"returns":[{"sku":"r","qty":1}]}"""),
			Lines,
			new SourceMember("returns", ["returns"]));

		_ = Skus(rows[0].Lines).Should().Be("ax1");
		_ = Skus(rows[0].Returns).Should().Be("rx1");
	}

	[Test]
	public void ReadRows_ASingleObjectWhereTheMemberIsAList_BecomesAListOfOne()
	{
		// Elasticsearch accepts one object where the mapping expects several
		var rows = ReadRows<SourcedOrderDto>(Response("""{"lines":{"sku":"solo","qty":3}}"""), Lines);

		_ = Skus(rows[0].Lines).Should().Be("solox3");
	}

	[Test]
	public void ReadRows_ANullInTheDocument_KeepsTheInitializer()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("""{"lines":null}"""), Lines);

		_ = rows[0].Lines.Should().NotBeNull().And.BeEmpty();
	}

	[Test]
	public void ReadRows_AMemberTheDocumentDoesNotHold_KeepsTheInitializer()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("""{"reference":"A1"}"""), Lines);

		_ = rows[0].Lines.Should().NotBeNull().And.BeEmpty();
	}

	[Test]
	public void ReadRows_APathBelowTheTopOfTheDocument_IsFollowed()
	{
		// the top-level lines are a different member, and must not be taken
		var rows = ReadRows<SourcedOrderDto>(
			Response("""{"shipping":{"lines":[{"sku":"n","qty":9}]},"lines":[{"sku":"top","qty":0}]}"""),
			new SourceMember("lines", ["shipping", "lines"]));

		_ = Skus(rows[0].Lines).Should().Be("nx9");
	}

	[Test]
	public void ReadRows_AnObjectFromTheDocument_IsReadWhole()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("""{"first":{"sku":"f","qty":1}}"""), new SourceMember("first", ["first"]));

		_ = rows[0].First.Should().NotBeNull();
		_ = rows[0].First!.Sku.Should().Be("f");
	}

	[Test]
	public void ReadRows_ANullSourceCell_LeavesTheMemberAsInitialized()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("null"), Lines);

		_ = rows[0].Reference.Should().Be("A1");
		_ = rows[0].Lines.Should().BeEmpty();
	}

	[Test]
	public void ReadRows_EachRowReadsItsOwnDocument()
	{
		var rows = ReadRows<SourcedOrderDto>(Response("""{"lines":[{"sku":"a","qty":1}]}""", "{}"), Lines);

		_ = Skus(rows[0].Lines).Should().Be("ax1");
		_ = rows[1].Reference.Should().Be("A2");
		_ = rows[1].Lines.Should().BeEmpty();
	}

	[Test]
	public async Task Query_AMemberDeclaredFromTheDocument_ArrivesFilledSyncAndAsync()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = Response("""{"lines":[{"sku":"a","qty":1},{"sku":"b","qty":2}]}""")
		};
		var provider = new EsqlQueryProvider(
			new JsonSerializerOptions { TypeInfoResolver = EsqlTestMappingContext.Default, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
			executor);

		var query = new EsqlQueryable<SourcedOrder>(provider)
			.From("orders", MetadataField.Source)
			.Select(o => new SourcedOrderDto { Reference = o.Reference, Lines = EsqlMetadata.SourceAs(o.Lines) });

		_ = Skus(query.ToList()[0].Lines).Should().Be("ax1,bx2");
		_ = Skus((await query.AsEsqlQueryable().ToListAsync())[0].Lines).Should().Be("ax1,bx2");
	}

	private static readonly SourceMember WholeRow = new(string.Empty, []);

	[Test]
	public void ReadRows_TheDocumentAsTheRow_FillsEveryMember()
	{
		var rows = ReadRows<SourcedOrder>(
			"""{"columns":[{"name":"_source","type":"_source"}],"values":[[{"reference":"A1","total":9.5,"lines":[{"sku":"a","qty":1}],"shipping":{"lines":[{"sku":"s","qty":2}]}}]]}""",
			WholeRow);

		_ = rows[0].Reference.Should().Be("A1");
		_ = rows[0].Total.Should().Be(9.5);
		_ = Skus(rows[0].Lines).Should().Be("ax1");
		_ = Skus(rows[0].Shipping!.Lines).Should().Be("sx2");
	}

	[Test]
	public void ReadRows_TheDocumentAsTheRow_WrapsASingleObjectAndKeepsInitializersOverNull()
	{
		var rows = ReadRows<SourcedOrder>(
			"""{"columns":[{"name":"_source","type":"_source"}],"values":[[{"reference":"A1","lines":{"sku":"solo","qty":3},"returns":null}]]}""",
			WholeRow);

		_ = Skus(rows[0].Lines).Should().Be("solox3");
		_ = rows[0].Returns.Should().NotBeNull().And.BeEmpty();
	}

	[Test]
	public async Task Query_TheWholeRowFromTheDocument_ArrivesFilledSyncAndAsync()
	{
		var executor = new CapturingQueryExecutor
		{
			ResponseJson = """{"columns":[{"name":"_source","type":"_source"}],"values":[[{"reference":"A1","lines":[{"sku":"a","qty":1},{"sku":"b","qty":2}]}]]}"""
		};
		var provider = new EsqlQueryProvider(
			new JsonSerializerOptions { TypeInfoResolver = EsqlTestMappingContext.Default, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
			executor);

		var query = new EsqlQueryable<SourcedOrder>(provider)
			.From("orders", MetadataField.Source)
			.Select(o => EsqlMetadata.SourceAs<SourcedOrder>());

		_ = query.ToString().Should().Be(
			"""
            FROM orders METADATA _source
            | KEEP _source
            """.NativeLineEndings());
		_ = Skus(query.ToList()[0].Lines).Should().Be("ax1,bx2");
		_ = Skus((await query.AsEsqlQueryable().ToListAsync())[0].Lines).Should().Be("ax1,bx2");
	}
}
