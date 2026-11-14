// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Translation;

namespace Elastic.Esql.Tests.Translation;

public class PropertyConverterOptionsTests
{
	[Test]
	public void FormatValue_TwoPropertiesWithDifferentlyConfiguredConverters_FormatsEachThroughItsOwn()
	{
		var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
		options.MakeReadOnly();
		var context = new EsqlTranslationContext { Metadata = new JsonMetadataManager(options), InlineParameters = true };

		var doubled = context.FormatValue(5, typeof(ScaledModel).GetProperty(nameof(ScaledModel.Doubled)));
		var tripled = context.FormatValue(5, typeof(ScaledModel).GetProperty(nameof(ScaledModel.Tripled)));

		doubled.Should().Be("10");
		tripled.Should().Be("15");
	}

	private sealed class ScaledModel
	{
		[Scale(2)]
		public int Doubled { get; set; }

		[Scale(3)]
		public int Tripled { get; set; }
	}

	private sealed class ScaleAttribute(int factor) : JsonConverterAttribute
	{
		public override JsonConverter? CreateConverter(Type typeToConvert) => new ScaleConverter(factor);
	}

	private sealed class ScaleConverter(int factor) : JsonConverter<int>
	{
		public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetInt32() / factor;

		public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value * factor);
	}
}
