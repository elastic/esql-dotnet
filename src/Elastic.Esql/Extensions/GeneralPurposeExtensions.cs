// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Extensions;

public static class GeneralPurposeExtensions
{
	public static T MultiField<T>(this T field, string multiFieldName)
	{
		_ = field;
		_ = multiFieldName;

		throw new InvalidOperationException("Only valid in ESQL queries.");
	}
}
