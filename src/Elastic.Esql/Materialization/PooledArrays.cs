// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;

namespace Elastic.Esql.Materialization;

/// <summary>Growth step shared by the buffers rented from <see cref="ArrayPool{T}.Shared"/>.</summary>
internal static class PooledArrays
{
	/// <summary>
	/// Swaps <paramref name="rented"/> for a pooled array of at least <paramref name="minimumLength"/> elements that starts
	/// with the <paramref name="length"/> elements found at <paramref name="start"/>, and returns the old array with its
	/// entries cleared when <paramref name="clearArray"/> is set.
	/// </summary>
	public static void Grow<T>(ref T[] rented, int start, int length, int minimumLength, bool clearArray = false)
	{
		var grown = ArrayPool<T>.Shared.Rent(minimumLength);

		// The copy runs before the return, because the pool may hand the old array to another renter at once.
		rented.AsSpan(start, length).CopyTo(grown);
		ArrayPool<T>.Shared.Return(rented, clearArray);
		rented = grown;
	}
}
