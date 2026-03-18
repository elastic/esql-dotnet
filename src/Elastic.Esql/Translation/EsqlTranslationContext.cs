// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Elastic.Esql.Core;
using Elastic.Esql.Formatting;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Translation;

internal sealed class EsqlTranslationContext
{
	public required JsonMetadataManager Metadata { get; init; }
	public required bool InlineParameters { get; init; }

	public JsonSerializerOptions SerializerOptions => Metadata.Options;

	public Type? ElementType { get; set; }
	public List<QueryCommand> Commands { get; } = [];
	public EsqlParameters Parameters { get; } = new();
	public object? QueryOptions { get; set; }

	private Dictionary<Type, HashSet<string>>? _anonymousTypeFields;

	/// <summary>
	/// Resolves a field name from a declaring type and member, handling anonymous types
	/// by applying <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> instead of
	/// looking up registered type metadata.
	/// </summary>
	public string ResolveFieldName(Type declaringType, MemberInfo member) =>
		declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
			? SerializerOptions.PropertyNamingPolicy?.ConvertName(member.Name) ?? member.Name
			: Metadata.ResolvePropertyName(declaringType, member);

	/// <summary>
	/// Registers the resolved field names for an anonymous type, extracted from a <see cref="NewExpression"/>.
	/// </summary>
	public void RegisterAnonymousTypeFields(Type type, HashSet<string> fieldNames)
	{
		_anonymousTypeFields ??= [];
		_anonymousTypeFields[type] = fieldNames;
	}

	/// <summary>
	/// Returns true when field names were explicitly tracked for an anonymous type
	/// during projection translation.
	/// </summary>
	public bool IsTrackedAnonymousType(Type type) =>
		_anonymousTypeFields is not null && _anonymousTypeFields.ContainsKey(type);

	/// <summary>
	/// Tries to retrieve tracked field names for a type. Returns the registered set for
	/// anonymous types, or resolves all property names from the metadata manager for concrete types.
	/// </summary>
	public HashSet<string> GetAllFieldNames(Type type)
	{
		if (_anonymousTypeFields is not null && _anonymousTypeFields.TryGetValue(type, out var tracked))
			return tracked;

		return Metadata.GetAllPropertyNames(type);
	}

	/// <summary>
	/// Returns a formatted string representing either the provided value or the parameter name, depending on the current
	/// parameter inlining mode. Values are serialized via <see cref="JsonSerializer"/> to respect user-configured
	/// <see cref="JsonSerializerOptions"/> (e.g. enum converters, custom converters).
	/// </summary>
	/// <param name="name">The name of the parameter to use in the formatted output.</param>
	/// <param name="value">The value to format or associate with the parameter name.</param>
	/// <param name="propertyContext">Optional property member whose custom <see cref="JsonConverter"/> should be respected.</param>
	/// <returns>A string containing either the formatted value or a parameter reference, based on whether parameters are inlined.</returns>
	public string GetValueOrParameterName(string name, object? value, MemberInfo? propertyContext = null)
	{
		if (InlineParameters)
			return FormatValue(value, propertyContext);

		var element = SerializeToElement(value, propertyContext);
		return $"?{Parameters.Add(name, element)}";
	}

	/// <summary>
	/// Formats a value as an ES|QL literal. When a property-level custom converter
	/// is present, the value is serialized through <see cref="JsonSerializer"/> to honour that converter;
	/// otherwise it is formatted via <see cref="EsqlFormatting.FormatValue"/>.
	/// </summary>
	public string FormatValue(object? value, MemberInfo? propertyContext = null)
	{
		if (value is not null && Metadata.FindPropertyConverter(propertyContext) is not null)
			return EsqlFormatting.FormatJsonElement(SerializeToElement(value, propertyContext));

		return EsqlFormatting.FormatValue(value, SerializerOptions);
	}

	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialization delegates to the user-provided JsonSerializerOptions/JsonSerializerContext which is expected to include an AOT-safe TypeInfoResolver.")]
	private JsonElement SerializeToElement(object? value, MemberInfo? propertyContext = null)
	{
		value = value switch
		{
			float f when float.IsNaN(f) || float.IsInfinity(f) => null,
			double d when double.IsNaN(d) || double.IsInfinity(d) => null,
			TimeSpan ts => EsqlFormatting.FormatTimeSpanRaw(ts),
			_ => value
		};

		if (value is not null && Metadata.FindPropertyConverter(propertyContext) is { } converter)
			return JsonSerializer.SerializeToElement(value, value.GetType(), Metadata.GetOptionsWithConverter(converter));

		return JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), SerializerOptions);
	}
}
