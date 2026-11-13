// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Tests.Core;

public class ProviderOptionsTests
{
	[Test]
	public void Metadata_DefaultProvider_HasReadOnlyOptions() =>
		new EsqlQueryProvider().Metadata.Options.IsReadOnly.Should().BeTrue();

	[Test]
	public void Metadata_ContextProvider_HasReadOnlyOptions() =>
		new EsqlQueryProvider(MaterializationTestJsonContext.Default).Metadata.Options.IsReadOnly.Should().BeTrue();

	[Test]
	public void Metadata_CallerOptions_StayMutable()
	{
		var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

		_ = new EsqlQueryProvider(options);

		options.IsReadOnly.Should().BeFalse();
	}

	[Test]
	public void Translate_OptionsWithoutResolver_ResolvesNestedMember()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

		var query = new EsqlQueryable<NestedHostLookup>(new EsqlQueryProvider(options))
			.From("logs")
			.Where(l => l.Host.Name == "ada");

		query.ToString().Should().Be(
			"""
			FROM logs
			| WHERE host.name == "ada"
			""".NativeLineEndings());
	}

	[Test]
	public void Translate_CallerOptions_StayMutable()
	{
		var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

		_ = new EsqlQueryable<NestedHostLookup>(new EsqlQueryProvider(options))
			.From("logs")
			.Where(l => l.Host.Name == "ada")
			.ToString();

		options.IsReadOnly.Should().BeFalse();
	}
}
