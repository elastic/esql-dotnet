// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

using Elastic.Esql.Serialization;

namespace Elastic.Esql.Vectors;

/// <summary>
/// Wrapper for a dense vector of <see cref="float"/> values used with ES|QL <c>dense_vector</c>
/// fields. Carries a <see cref="JsonConverterAttribute"/> so the configured encoding
/// (legacy array or base64) is applied even when the value is captured from a closure.
/// </summary>
[JsonConverter(typeof(FloatVectorJsonConverter))]
public readonly struct FloatVector(ReadOnlyMemory<float> data) : IEquatable<FloatVector>
{
	/// <summary>The underlying vector data.</summary>
	public ReadOnlyMemory<float> Data { get; } = data;

	/// <summary>True if the vector contains no values.</summary>
	public bool IsEmpty => Data.IsEmpty;

	/// <summary>The number of dimensions.</summary>
	public int Length => Data.Length;

	public static implicit operator FloatVector(float[] data) => new(data);

	public static implicit operator FloatVector(ReadOnlyMemory<float> data) => new(data);

	public static implicit operator FloatVector(List<float> data) => new(data.ToArray());

	public bool Equals(FloatVector other) => Data.Equals(other.Data);

	public override bool Equals(object? obj) => obj is FloatVector other && Equals(other);

	public override int GetHashCode() => Data.GetHashCode();

	public static bool operator ==(FloatVector left, FloatVector right) => left.Equals(right);

	public static bool operator !=(FloatVector left, FloatVector right) => !left.Equals(right);
}
