// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Elastic.Esql.Core;

/// <summary>AOT-safe type introspection helpers.</summary>
internal static class TypeHelper
{
	/// <summary>
	/// Returns <see langword="true"/> when <paramref name="type"/> implements <see cref="IEnumerable{T}"/> for some <c>T</c>, excluding <see cref="string"/>
	/// (which implements <c>IEnumerable&lt;char&gt;</c>).
	/// </summary>
	internal static bool IsEnumerableType(Type type) =>
		type != typeof(string) && FindGenericType(typeof(IEnumerable<>), type) is not null;

	/// <summary>
	/// Searches the inheritance hierarchy of a given type to locate a constructed generic type
	/// that matches the specified generic type definition.
	/// </summary>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070:RequiresUnreferencedCode",
		Justification = "GetInterfaces is only called if 'definition' is interface type. " +
						"In that case though the interface must be present (otherwise the Type of it could not exist) " +
						"which also means that the trimmer kept the interface and thus kept it on all types " +
						"which implement it. It doesn't matter if the GetInterfaces call below returns fewer types" +
						"as long as it returns the 'definition' as well.")]
	internal static Type? FindGenericType(Type definition, Type? type)
	{
		bool? definitionIsInterface = null;

		while (type is not null && type != typeof(object))
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == definition)
				return type;

			definitionIsInterface ??= definition.IsInterface;

			if (definitionIsInterface.GetValueOrDefault())
			{
				foreach (var itype in type.GetInterfaces())
				{
					var found = FindGenericType(definition, itype);
					if (found is not null)
						return found;
				}
			}

			type = type.BaseType;
		}

		return null;
	}
}
