// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Esql.Validation;

namespace Elastic.Esql.FieldMetadataResolver;

///  <summary>
///  A <c>System.Text.Json</c> based <see cref="IEsqlFieldNameResolver"/> implementation.
///  </summary>
///  <remarks>
///		This resolver resolves the field names exactly like the <c>System.Text.Json</c> serializer, honoring
///		<see cref="JsonSerializerOptions.PropertyNamingPolicy"/> and <see cref="JsonPropertyNameAttribute"/> attributes.
///  </remarks>
public sealed class SystemTextJsonFieldNameResolver : IEsqlFieldNameResolver // TODO: Context overload
{
	public JsonSerializerOptions Options { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="SystemTextJsonFieldNameResolver"/> class using <see cref="JsonSerializerOptions.Default"/>.
	/// </summary>
	/// <remarks>
	///	Use the <see cref="SystemTextJsonFieldNameResolver(JsonSerializerOptions)"/> or <see cref="SystemTextJsonFieldNameResolver(JsonSerializerContext)"/>
	/// for AOT compatibility.
	/// </remarks>
	public SystemTextJsonFieldNameResolver() => Options = JsonSerializerOptions.Default;

	/// <summary>
	/// Initializes a new instance of the <see cref="SystemTextJsonFieldNameResolver"/> class using the specified <see cref="JsonSerializerOptions"/>.
	/// </summary>
	/// <param name="options">The <see cref="JsonSerializerOptions"/> that provides metadata for JSON serialization and field name resolution.</param>
	public SystemTextJsonFieldNameResolver(JsonSerializerOptions? options)
	{
		Verify.NotNull(options);

		Options = options;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SystemTextJsonFieldNameResolver"/> class using the specified <see cref="JsonSerializerContext"/>.
	/// </summary>
	/// <param name="context">The <see cref="JsonSerializerContext"/> that provides metadata for JSON serialization and field name resolution.</param>
	public SystemTextJsonFieldNameResolver(JsonSerializerContext context)
	{
		Verify.NotNull(context);

		Options = new JsonSerializerOptions
		{
			TypeInfoResolver = context
		};
	}

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

	/// <inheritdoc/>
	public HashSet<string> GetAllFieldNames(Type type)
	{
		Verify.NotNull(type);

		var typeInfo = Options.GetTypeInfo(type);

		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var prop in typeInfo.Properties)
			_ = names.Add(prop.Name);

		return names;
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
