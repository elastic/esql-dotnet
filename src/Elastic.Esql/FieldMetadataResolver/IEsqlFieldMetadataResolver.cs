// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Reflection;

namespace Elastic.Esql.FieldMetadataResolver;

public interface IEsqlFieldMetadataResolver
{
	// TODO: Document
	string GetFieldName(Type type, MemberInfo member);

	/// <summary>
	/// Converts a raw property name to a field name using the configured naming policy.
	/// Used for anonymous types and other cases where no registered type metadata is available.
	/// </summary>
	string GetAnonymousFieldName(string name);
}
