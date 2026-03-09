// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Esql.Integration.Tests.Models;

public class TestCategoryLookup
{
	[JsonPropertyName("category_id")]
	public string CategoryId { get; set; } = string.Empty;

	[JsonPropertyName("category_label")]
	public string CategoryLabel { get; set; } = string.Empty;

	public string Region { get; set; } = string.Empty;
}
