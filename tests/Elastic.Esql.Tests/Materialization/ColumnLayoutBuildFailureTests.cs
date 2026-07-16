// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Materialization;

namespace Elastic.Esql.Tests.Materialization;

public class ColumnLayoutBuildFailureTests
{
	private sealed class ThrowingResolver : IJsonTypeInfoResolver
	{
		public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
			throw new FormatException("resolver bug");
	}

	[Test]
	public void Build_ResolverThrowsUnexpectedException_Propagates()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new ThrowingResolver() };
		var metadata = new JsonMetadataManager(options);
		var columns = new EsqlResponseReader.ColumnInfo[] { new("value", "keyword") };

		var act = () => ColumnLayout.Build(columns, typeof(ScalarStringModel), metadata);

		_ = act.Should().Throw<FormatException>();
	}
}
