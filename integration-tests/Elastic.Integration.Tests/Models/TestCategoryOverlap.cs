// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

/// <summary>
/// Lookup model whose <c>Name</c> field collides with <see cref="TestProduct.Name"/>
/// (both serialize to <c>name</c>). Used to test JOIN field collision handling.
/// </summary>
public class TestCategoryOverlap
{
	[JsonPropertyName("category_id")]
	public string CategoryId { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string Region { get; set; } = string.Empty;
}

// Result types for collision join tests — classes with init properties so that
// the LINQ result selector produces MemberInitExpression, which the translator
// inspects for field collision detection.

public class CollisionBothResult
{
	public string OuterName { get; init; } = string.Empty;
	public string InnerName { get; init; } = string.Empty;
}

public class CollisionOuterResult
{
	public string ProductName { get; init; } = string.Empty;
	public string Region { get; init; } = string.Empty;
}

public class CollisionInnerResult
{
	public string Name { get; init; } = string.Empty;
	public string Region { get; init; } = string.Empty;
}

public class CollisionOriginalNameResult
{
	public string Name { get; init; } = string.Empty;
	public string Region { get; init; } = string.Empty;
}
