// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
#if !NETSTANDARD2_0
using System.Runtime.CompilerServices;
#endif

namespace Elastic.Esql.Materialization;

/// <summary>Growth step shared by the buffers rented from <see cref="ArrayPool{T}.Shared"/>.</summary>
internal static class PooledArrays
{
	/// <summary>
	/// Swaps <paramref name="rented"/> for a pooled array of at least <paramref name="minimumLength"/> elements that starts
	/// with the <paramref name="length"/> elements found at <paramref name="start"/>, and returns the old array to the pool
	/// cleared when its elements hold references.
	/// </summary>
	public static void Grow<T>(ref T[] rented, int start, int length, int minimumLength)
	{
		var grown = ArrayPool<T>.Shared.Rent(minimumLength);

		// An array whose elements hold references goes back cleared, so the pool does not keep those objects reachable. An
		// array whose elements hold none skips a clear it does not need. netstandard2.0 lacks IsReferenceOrContainsReferences,
		// so there every element type that is not a primitive or an enum counts as holding references.
#if NETSTANDARD2_0
		var holdsReferences = !typeof(T).IsPrimitive && !typeof(T).IsEnum;
#else
		var holdsReferences = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
#endif

		// The copy runs before the return, because the pool may hand the old array to another renter at once.
		rented.AsSpan(start, length).CopyTo(grown);
		ArrayPool<T>.Shared.Return(rented, clearArray: holdsReferences);
		rented = grown;
	}
}
