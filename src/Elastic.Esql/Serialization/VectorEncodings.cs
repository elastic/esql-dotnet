// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Esql.Serialization;

/// <summary>The encoding to use when serializing <see cref="Vectors.FloatVector"/> data.</summary>
public enum FloatVectorEncoding
{
	/// <summary>Legacy (JSON array) vector encoding for backwards compatibility.</summary>
	Legacy,

	/// <summary>
	/// <c>Base64</c> vector encoding. Available starting from Elasticsearch 9.3.0.
	/// </summary>
	Base64
}

/// <summary>The encoding to use when serializing <see cref="Vectors.ByteVector"/> data.</summary>
public enum ByteVectorEncoding
{
	/// <summary>Legacy (JSON array) vector encoding for backwards compatibility.</summary>
	Legacy,

	/// <summary>Hexadecimal string vector encoding. Available starting from Elasticsearch 8.14.0.</summary>
	Hex,

	/// <summary>
	/// <c>Base64</c> vector encoding. Available starting from Elasticsearch 9.3.0.
	/// </summary>
	Base64
}

/// <summary>
/// Per-<see cref="System.Text.Json.JsonSerializerOptions"/> context that determines how vector
/// payloads are encoded by the vector converters. Attach via <see cref="ContextProvider{TContext}"/>.
/// </summary>
public sealed record EsqlVectorEncodingContext(
	FloatVectorEncoding FloatVectorEncoding = FloatVectorEncoding.Legacy,
	ByteVectorEncoding ByteVectorEncoding = ByteVectorEncoding.Legacy
);
