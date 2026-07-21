// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport;

namespace Elastic.Clients.Esql.Tests;

public class ClientTestDocument
{
	public string Name { get; set; } = string.Empty;
}

public class UnregisteredDocument
{
	public int Value { get; set; }
}

[JsonSerializable(typeof(ClientTestDocument))]
internal sealed partial class ClientTestJsonContext : JsonSerializerContext;

public class EsqlClientSettingsTests
{
	private static EsqlClientSettings CreateSettings(JsonSerializerOptions? options = null, JsonSerializerContext? context = null)
	{
		var invoker = new CapturingRequestInvoker("""{"columns":[],"values":[]}"""u8.ToArray());
		var config = new TransportConfiguration(
			new SingleNodePool(new Uri("http://localhost:9200")),
			invoker,
			productRegistration: EsqlProductRegistration.Default);

		return new EsqlClientSettings(new DistributedTransport(config))
		{
			JsonSerializerOptions = options,
			JsonSerializerContext = context
		};
	}

	[Test]
	public void Defaults_NewSettings_HasEmptyQueryDefaults()
	{
		var settings = CreateSettings();

		_ = settings.Defaults.Locale.Should().BeNull();
		_ = settings.Defaults.TimeZone.Should().BeNull();
	}

	[Test]
	public void ResolveJsonOptions_NoConfiguration_UsesCamelCaseNaming()
	{
		var resolved = CreateSettings().ResolveJsonOptions();

		_ = resolved.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
	}

	[Test]
	public void ResolveJsonOptions_ExplicitOptions_ReturnsSameInstance()
	{
		var custom = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

		var resolved = CreateSettings(options: custom).ResolveJsonOptions();

		_ = resolved.Should().BeSameAs(custom);
	}

	[Test]
	public void ResolveJsonOptions_ContextWithoutNamingPolicy_FallsBackToCamelCase()
	{
		var resolved = CreateSettings(context: ClientTestJsonContext.Default).ResolveJsonOptions();

		_ = resolved.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
		_ = resolved.GetTypeInfo(typeof(ClientTestDocument)).Should().NotBeNull();
	}

	[Test]
	public void ResolveJsonOptions_Context_CombinesReflectionFallbackForUnregisteredTypes()
	{
		var resolved = CreateSettings(context: ClientTestJsonContext.Default).ResolveJsonOptions();

		_ = resolved.GetTypeInfo(typeof(UnregisteredDocument)).Should().NotBeNull();
	}

	[Test]
	public void ResolveJsonOptions_ContextAndOptions_ContextTakesPrecedence()
	{
		var custom = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

		var resolved = CreateSettings(options: custom, context: ClientTestJsonContext.Default).ResolveJsonOptions();

		_ = resolved.Should().NotBeSameAs(custom);
		_ = resolved.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
	}
}
