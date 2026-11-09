// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Runtime.CompilerServices;

namespace Elastic.Esql.Materialization;

/// <summary>Whether the runtime can generate code, which compiled setters and property converter contracts need.</summary>
internal static class DynamicCode
{
	// netstandard2.0 has no RuntimeFeature.IsDynamicCodeSupported, but every runtime it targets here compiles expression trees.
#if NETSTANDARD2_0
	public static bool IsSupported => true;
#else
	public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;
#endif
}
