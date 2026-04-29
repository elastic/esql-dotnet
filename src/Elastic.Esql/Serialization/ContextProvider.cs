// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Esql.Serialization;

/// <summary>
/// Links a <typeparamref name="TContext"/> instance to a <see cref="JsonSerializerOptions"/> instance
/// by piggy-backing on the converters list. Custom converters can pull the context out via <see cref="GetContext"/>
/// or <see cref="TryGetContext"/> when they need access to per-options configuration.
/// </summary>
/// <remarks>
/// Slim port of <c>Elastic.Clients.Elasticsearch.Serialization.ContextProvider&lt;T&gt;</c>.
/// </remarks>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Static lookup mirrors elasticsearch-net pattern; per-context-type retrieval requires the generic.")]
public sealed class ContextProvider<TContext>(TContext context) : JsonConverterFactory
{
	private readonly Converter _converter = new(context);

	/// <summary>Retrieves the <typeparamref name="TContext"/> linked to <paramref name="options"/>.</summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Marker type lookup only")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Marker type lookup only")]
	public static TContext GetContext(JsonSerializerOptions options)
	{
		if (!TryGetContext(options, out var context))
			throw new InvalidOperationException(
				$"No context provider for type '{typeof(TContext).Name}' is registered for the given 'JsonSerializerOptions' instance.");

		return context;
	}

	/// <summary>Tries to retrieve the <typeparamref name="TContext"/> linked to <paramref name="options"/>.</summary>
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Marker type lookup only")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Marker type lookup only")]
	public static bool TryGetContext(JsonSerializerOptions options, [MaybeNullWhen(false)] out TContext context)
	{
		foreach (var converter in options.Converters)
		{
			if (converter is ContextProvider<TContext> global)
			{
				context = global._converter.Context;
				return true;
			}
		}

		if (options.GetConverter(typeof(Marker)) is Converter provider)
		{
			context = provider.Context;
			return true;
		}

		context = default;
		return false;
	}

	public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Marker);

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => _converter;

	private sealed class Marker;

	private sealed class Converter(TContext context) : JsonConverter<Marker>
	{
		public TContext Context { get; } = context;

		public override Marker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			throw new InvalidOperationException();

		public override void Write(Utf8JsonWriter writer, Marker value, JsonSerializerOptions options) =>
			throw new InvalidOperationException();
	}
}
