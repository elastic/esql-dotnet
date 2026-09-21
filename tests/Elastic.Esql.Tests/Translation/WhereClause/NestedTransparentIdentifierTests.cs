// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq.Expressions;

using Elastic.Esql.Translation;

namespace Elastic.Esql.Tests.Translation.WhereClause;

public class NestedTransparentIdentifierTests : EsqlTestBase
{
	[Test]
	public void Translate_WithNestedTransparentIdentifiers_StripsAllPrefixes()
	{
		var source = new[]
		{
			new { Outer = new { Outer = new LogEntry() } }
		}.AsQueryable();

		var query = source.Where(x => x.Outer.Outer.StatusCode >= 10091);
		var whereCall = (MethodCallExpression)query.Expression;
		var predicate = (LambdaExpression)((UnaryExpression)whereCall.Arguments[1]).Operand;

		var context = new EsqlTranslationContext
		{
			Metadata = QueryProvider.Metadata,
			InlineParameters = true
		};

		var visitor = new WhereClauseVisitor(context);
		var translated = visitor.Translate(predicate.Body);

		_ = translated.Should().Be("statusCode >= 10091");
	}

	[Test]
	public void Translate_MultiValuePredicateThroughTransparentIdentifiers_StripsPrefixes()
	{
		var source = new[] { new { Outer = new { Outer = new TaggedProduct() } } }.AsQueryable();

		var query = source.Where(x => x.Outer.Outer.Tags.Any(t => t.StartsWith("a")));
		var whereCall = (MethodCallExpression)query.Expression;
		var predicate = (LambdaExpression)((UnaryExpression)whereCall.Arguments[1]).Operand;

		var context = new EsqlTranslationContext { Metadata = QueryProvider.Metadata, InlineParameters = true, MultiValueLimit = 4 };
		var translated = new WhereClauseVisitor(context).Translate(predicate.Body);

		_ = translated.Should().NotContain("outer");
	}

	[Test]
	public void Translate_MatchThroughTransparentIdentifiers_StripsPrefixes()
	{
		var source = new[] { new { Outer = new TaggedProduct() } }.AsQueryable();

		var query = source.Where(x => x.Outer.Tags.Contains("a"));
		var whereCall = (MethodCallExpression)query.Expression;
		var predicate = (LambdaExpression)((UnaryExpression)whereCall.Arguments[1]).Operand;

		var context = new EsqlTranslationContext { Metadata = QueryProvider.Metadata, InlineParameters = true };
		var translated = new WhereClauseVisitor(context).Translate(predicate.Body);

		_ = translated.Should().NotContain("outer");
	}
}
