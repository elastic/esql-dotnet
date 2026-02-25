// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Validation;

namespace Elastic.Esql.FieldMetadataResolver;

/// <summary>
/// A <c>System.Text.Json</c> based <see cref="IEsqlFieldMetadataResolver"/> implementation.
/// </summary>
/// <remarks>
///	This resolver resolves the field names exactly like the <c>System.Text.Json</c> serializer, honoring
/// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> and <see cref="JsonPropertyNameAttribute"/> attributes.
/// </remarks>
/// <param name="options">The <see cref="JsonSerializerOptions"/> instance.</param>
public sealed class SystemTextJsonFieldMetadataResolver(JsonSerializerOptions? options) : IEsqlFieldMetadataResolver
{
	public JsonSerializerOptions Options { get; } = options ?? JsonSerializerOptions.Default;

	public string GetFieldName(Type type, MemberInfo member)
	{
		Verify.NotNull(type);
		Verify.NotNull(member);

		return FindProperty(type, member).Name;
	}

	/// <inheritdoc/>
	public string GetAnonymousFieldName(string name)
	{
		Verify.NotNullOrEmpty(name);

		return Options.PropertyNamingPolicy?.ConvertName(name) ?? name;
	}

	private JsonPropertyInfo FindProperty(Type type, MemberInfo member)
	{
		Verify.NotNull(type);
		Verify.NotNull(member);

		var typeInfo = Options.GetTypeInfo(type);
		var property = typeInfo.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo mi && mi == member);

		return property ?? throw new NotSupportedException($"Member '{member.Name}' of type '{member.DeclaringType?.Name}' is not supported.");
	}
}
