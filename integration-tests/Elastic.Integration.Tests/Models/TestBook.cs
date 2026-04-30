// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

using Elastic.Esql.Vectors;

namespace Elastic.Esql.Integration.Tests.Models;

/// <summary>
/// Test document with <c>dense_vector</c> fields used by KNN, V_*, FORK / FUSE,
/// and METADATA integration tests.
/// </summary>
public class TestBook
{
	[JsonPropertyName("book_id")]
	public string Id { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	[JsonPropertyName("title_vec")]
	public FloatVector TitleVec { get; set; }

	[JsonPropertyName("rgb_vector")]
	public ByteVector RgbVector { get; set; }
}

/// <summary>Result projection that exposes the document <c>_id</c> and <c>_score</c> via <see cref="EsqlMetadata"/> markers.</summary>
public class BookIdScore
{
	public string Id { get; set; } = string.Empty;
	public float Score { get; set; }
}

/// <summary>Result projection used to verify <see cref="EsqlMetadata.Id"/> rename + regular field combination.</summary>
public class BookIdTitle
{
	public string Id { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
}

/// <summary>Result projection used to verify <see cref="EsqlMetadata.SourceAs{T}"/> deserialisation.</summary>
public class BookSourceProjection
{
	public TestBook? Original { get; set; }
}

/// <summary>Result projection used by the FUSE fork-discriminator test.</summary>
public class ForkResult
{
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("_fork")]
	public string Fork { get; set; } = string.Empty;
}
