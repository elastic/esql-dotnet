// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Esql.Integration.Tests.Models;

namespace Elastic.Esql.Integration.Tests.Infrastructure;

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	UseStringEnumConverter = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestProduct))]
[JsonSerializable(typeof(TestOrder))]
[JsonSerializable(typeof(TestEvent))]
[JsonSerializable(typeof(TestCategoryLookup))]
[JsonSerializable(typeof(TestCategoryOverlap))]
[JsonSerializable(typeof(CollisionBothResult))]
[JsonSerializable(typeof(CollisionOuterResult))]
[JsonSerializable(typeof(CollisionInnerResult))]
[JsonSerializable(typeof(CollisionOriginalNameResult))]
[JsonSerializable(typeof(TypeWithPropertyConverter))]
[JsonSerializable(typeof(TestUserProfile))]
[JsonSerializable(typeof(RawProductSummary))]
public partial class IntegrationJsonContext : JsonSerializerContext;
