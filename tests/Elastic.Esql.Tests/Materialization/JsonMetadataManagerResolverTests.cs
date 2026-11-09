// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization.Metadata;

namespace Elastic.Esql.Tests.Materialization;

public class JsonMetadataManagerResolverTests
{
	[Test]
	public void IsModifierFreeResolver_DefaultResolverWithoutModifiers_IsTrue() =>
		JsonMetadataManager.IsModifierFreeResolver(new DefaultJsonTypeInfoResolver()).Should().BeTrue();

	[Test]
	public void IsModifierFreeResolver_DefaultResolverWithOneModifier_IsFalse() =>
		JsonMetadataManager.IsModifierFreeResolver(new DefaultJsonTypeInfoResolver { Modifiers = { static _ => { } } }).Should().BeFalse();

	[Test]
	public void IsModifierFreeResolver_SourceGeneratedContext_IsTrue() =>
		JsonMetadataManager.IsModifierFreeResolver(MaterializationTestJsonContext.Default).Should().BeTrue();

	[Test]
	public void IsModifierFreeResolver_DefaultResolverSubclass_IsFalse() =>
		JsonMetadataManager.IsModifierFreeResolver(new DerivedResolver()).Should().BeFalse();

	[Test]
	public void IsModifierFreeResolver_CombinedResolver_IsFalse() =>
		JsonMetadataManager
			.IsModifierFreeResolver(JsonTypeInfoResolver.Combine(MaterializationTestJsonContext.Default, new DefaultJsonTypeInfoResolver()))
			.Should()
			.BeFalse();

	private sealed class DerivedResolver : DefaultJsonTypeInfoResolver;
}
