// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

/// <summary>
/// Model with a nested object property for testing ES|QL dot-notation column deserialization.
/// ES|QL returns "address.street", "address.city" as separate columns for object fields.
/// </summary>
public class TestUserProfile
{
	[JsonPropertyName("user_id")]
	public string UserId { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public TestAddress? Address { get; set; }
}

public class TestAddress
{
	public string Street { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
	public string Country { get; set; } = string.Empty;
}
