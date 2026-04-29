// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

using Elastic.Esql.Serialization;

namespace Elastic.Esql.Vectors;

/// <summary>
/// Wrapper for a dense vector of <see cref="byte"/> values used with ES|QL <c>dense_vector</c>
/// fields with byte element type. Carries a <see cref="JsonConverterAttribute"/> so the
/// configured encoding (legacy array, hex, or base64) is applied even when the value is
/// captured from a closure.
/// </summary>
[JsonConverter(typeof(ByteVectorJsonConverter))]
public readonly struct ByteVector(ReadOnlyMemory<byte> data) : IEquatable<ByteVector>
{
	/// <summary>The underlying vector data.</summary>
	public ReadOnlyMemory<byte> Data { get; } = data;

	/// <summary>True if the vector contains no values.</summary>
	public bool IsEmpty => Data.IsEmpty;

	/// <summary>The number of dimensions.</summary>
	public int Length => Data.Length;

	public static implicit operator ByteVector(byte[] data) => new(data);

	public static implicit operator ByteVector(ReadOnlyMemory<byte> data) => new(data);

	public static implicit operator ByteVector(List<byte> data) => new(data.ToArray());

	public bool Equals(ByteVector other) => Data.Equals(other.Data);

	public override bool Equals(object? obj) => obj is ByteVector other && Equals(other);

	public override int GetHashCode() => Data.GetHashCode();

	public static bool operator ==(ByteVector left, ByteVector right) => left.Equals(right);

	public static bool operator !=(ByteVector left, ByteVector right) => !left.Equals(right);
}
