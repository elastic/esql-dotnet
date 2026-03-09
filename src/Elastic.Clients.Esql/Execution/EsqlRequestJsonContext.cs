// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Clients.Esql.Execution;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EsqlRequest))]
[JsonSerializable(typeof(EsqlAsyncRequest))]
internal sealed partial class EsqlRequestJsonContext : JsonSerializerContext;
