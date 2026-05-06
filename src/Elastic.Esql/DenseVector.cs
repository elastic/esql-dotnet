// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

using Elastic.Esql.Serialization;

namespace Elastic.Esql;

/// <summary>
/// Strongly-typed wrapper around an Elasticsearch <c>dense_vector</c> value. Two element types
/// are supported:
/// <list type="bullet">
///   <item><description><see cref="DenseVector{T}"/> with <c>T = float</c> for <c>element_type: "float"</c>.</description></item>
///   <item><description><see cref="DenseVector{T}"/> with <c>T = byte</c> for both <c>element_type: "byte"</c>
///     and <c>element_type: "bit"</c>. The wire format is identical (a JSON array of signed bytes);
///     for bit vectors, callers are responsible for the bit-packing semantics (8 bits per byte —
///     a 16-dim bit vector is a <see cref="DenseVector{T}"/> of length 2).</description></item>
/// </list>
/// <para>
/// Implicit conversions from <see cref="ReadOnlyMemory{T}"/> and <c>T[]</c> let callers pass natural
/// values (e.g. RGB <c>new byte[] { 255, 0, 0 }</c>); the JSON converter handles the signed-byte
/// wire encoding for <c>T = byte</c>.
/// </para>
/// </summary>
[JsonConverter(typeof(DenseVectorJsonConverterFactory))]
public readonly struct DenseVector<T> : IEquatable<DenseVector<T>>
	where T : struct
{
	/// <summary>Creates a vector backed by <paramref name="values"/>.</summary>
	public DenseVector(ReadOnlyMemory<T> values) => Memory = values;

	/// <summary>Creates a vector backed by <paramref name="values"/>.</summary>
	public DenseVector(T[] values) => Memory = values;

	/// <summary>The underlying memory buffer.</summary>
	public ReadOnlyMemory<T> Memory { get; }

	/// <summary>A read-only span over the vector elements.</summary>
	public ReadOnlySpan<T> Span => Memory.Span;

	/// <summary>The number of elements in the vector.</summary>
	public int Length => Memory.Length;

	/// <summary>True when the vector has no elements.</summary>
	public bool IsEmpty => Memory.IsEmpty;

	/// <summary>Copies the vector elements into a new array.</summary>
	public T[] ToArray() => Memory.ToArray();

	/// <summary>Implicit conversion from <see cref="ReadOnlyMemory{T}"/>.</summary>
	public static implicit operator DenseVector<T>(ReadOnlyMemory<T> values) => new(values);

	/// <summary>Implicit conversion from <c>T[]</c>.</summary>
	public static implicit operator DenseVector<T>(T[] values) => new(values);

	/// <summary>Compares two vectors by underlying memory identity (same array, offset, length).</summary>
	public bool Equals(DenseVector<T> other) => Memory.Equals(other.Memory);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is DenseVector<T> other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => Memory.GetHashCode();

	/// <inheritdoc/>
	public override string ToString() => $"DenseVector<{typeof(T).Name}>[{Length}]";

	public static bool operator ==(DenseVector<T> left, DenseVector<T> right) => left.Equals(right);
	public static bool operator !=(DenseVector<T> left, DenseVector<T> right) => !left.Equals(right);
}
