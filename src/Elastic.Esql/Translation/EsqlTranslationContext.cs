// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

using Elastic.Esql.FieldMetadataResolver;
using Elastic.Esql.Formatting;
using Elastic.Esql.QueryModel;

namespace Elastic.Esql.Translation;

internal sealed class EsqlTranslationContext
{
	public required IEsqlFieldNameResolver FieldNameResolver { get; init; }
	public required bool InlineParameters { get; init; }

	public Type? ElementType { get; set; }
	public List<QueryCommand> Commands { get; } = [];
	public EsqlParameters Parameters { get; } = new();

	private Dictionary<Type, HashSet<string>>? _anonymousTypeFields;

	/// <summary>
	/// Resolves a field name from a declaring type and member, handling anonymous types
	/// by delegating to <see cref="IEsqlFieldNameResolver.GetAnonymousFieldName"/> instead of
	/// <see cref="IEsqlFieldNameResolver.GetFieldName"/> which requires registered type metadata.
	/// </summary>
	public string ResolveFieldName(Type declaringType, MemberInfo member) =>
		declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
			? FieldNameResolver.GetAnonymousFieldName(member.Name)
			: FieldNameResolver.GetFieldName(declaringType, member);

	/// <summary>
	/// Registers the resolved field names for an anonymous type, extracted from a <see cref="NewExpression"/>.
	/// </summary>
	public void RegisterAnonymousTypeFields(Type type, HashSet<string> fieldNames)
	{
		_anonymousTypeFields ??= [];
		_anonymousTypeFields[type] = fieldNames;
	}

	/// <summary>
	/// Tries to retrieve tracked field names for a type. Returns the registered set for
	/// anonymous types, or calls <see cref="IEsqlFieldNameResolver.GetAllFieldNames"/>
	/// for concrete types.
	/// </summary>
	public HashSet<string> GetAllFieldNames(Type type)
	{
		if (_anonymousTypeFields is not null && _anonymousTypeFields.TryGetValue(type, out var tracked))
			return tracked;

		return FieldNameResolver.GetAllFieldNames(type);
	}

	/// <summary>
	/// Returns a formatted string representing either the provided value or the parameter name, depending on the current
	/// parameter inlining mode.
	/// </summary>
	/// <remarks>
	/// If parameters are not inlined, the returned string will reference the parameter by name. If parameters are inlined, the value is formatted directly
	/// into the string.
	/// </remarks>
	/// <param name="name">The name of the parameter to use in the formatted output.</param>
	/// <param name="value">The value to format or associate with the parameter name.</param>
	/// <returns>A string containing either the formatted value or a parameter reference, based on whether parameters are inlined.</returns>
	public string GetValueOrParameterName(string name, object? value) =>
		InlineParameters
			? EsqlFormatting.FormatValue(value) : $"?{Parameters.Add(name, value)}";
}
