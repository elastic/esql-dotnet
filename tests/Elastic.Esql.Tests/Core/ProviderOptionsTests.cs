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
}
