// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Bogus;
using Elastic.Examples.Domain.Models;

namespace Elastic.Examples.Ingest.Generators;

/// <summary>Generates fake ApplicationLog data for testing and demos.</summary>
public static class ApplicationLogGenerator
{
	private const int Seed = 12345;

	private static readonly string[] Services = ["api-gateway", "order-service", "payment-service", "inventory-service", "notification-service"];
	private static readonly string[] Loggers = ["Microsoft.AspNetCore.Hosting", "Elastic.Clients", "OrderProcessor", "PaymentHandler", "InventoryManager"];
	private static readonly string[] Environments = ["production", "staging", "development"];
	private static readonly string[] HostNames = ["web-01", "web-02", "api-01", "api-02", "worker-01"];
	private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH"];
	private static readonly string[] UrlPaths = ["/api/orders", "/api/products", "/api/customers", "/api/payments", "/api/inventory", "/health", "/metrics"];
	private static readonly string[] Actions = ["create", "update", "delete", "fetch", "process", "validate", "notify"];
	private static readonly string[] ErrorTypes = ["NullReferenceException", "InvalidOperationException", "TimeoutException", "HttpRequestException", "ValidationException"];

	private static readonly string[] InfoMessages =
	[
		"Request completed successfully",
		"Order processed",
		"Payment authorized",
		"Inventory updated",
		"Notification sent",
		"Cache refreshed",
		"Connection established",
		"Session started"
	];

	private static readonly string[] WarnMessages =
	[
		"Slow query detected",
		"Rate limit approaching",
		"Retry attempt",
		"Connection pool near capacity",
		"Cache miss",
		"Deprecated API usage"
	];

	private static readonly string[] ErrorMessages =
	[
		"Failed to process order",
		"Payment authorization failed",
		"Database connection timeout",
		"External service unavailable",
		"Validation failed",
		"Insufficient inventory"
	];

	public static IReadOnlyList<ApplicationLog> Generate(
		IReadOnlyList<string>? orderIds = null,
		IReadOnlyList<string>? productIds = null,
		IReadOnlyList<string>? customerIds = null,
		int count = 10000)
	{
		Randomizer.Seed = new Random(Seed);
		var faker = new Faker();
		var logs = new List<ApplicationLog>(count);

		var logLevels = new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal };
		var weights = new[] { 0.05f, 0.15f, 0.55f, 0.15f, 0.08f, 0.02f };

		for (var i = 0; i < count; i++)
		{
			var level = faker.Random.WeightedRandom(logLevels, weights);
			var hasHttpInfo = faker.Random.Bool(0.7f);
			var traceId = faker.Random.Bool(0.8f) ? faker.Random.Guid().ToString("N") : null;
			var httpMethod = hasHttpInfo ? faker.PickRandom(HttpMethods) : null;

			var message = level switch
			{
				LogLevel.Error or LogLevel.Fatal => faker.PickRandom(ErrorMessages),
				LogLevel.Warn => faker.PickRandom(WarnMessages),
				_ => faker.PickRandom(InfoMessages)
			};

			logs.Add(new ApplicationLog
			{
				Timestamp = faker.Date.Recent(7),
				Level = level,
				Logger = faker.PickRandom(Loggers),
				Message = message,
				ErrorMessage = level >= LogLevel.Error ? faker.Lorem.Sentence() : null,
				StackTrace = level >= LogLevel.Error && faker.Random.Bool(0.7f)
					? $"at {faker.PickRandom(Services)}.{faker.Lorem.Word()}() in {faker.System.FilePath()}"
					: null,
				ErrorType = level >= LogLevel.Error ? faker.PickRandom(ErrorTypes) : null,
				ServiceName = faker.PickRandom(Services),
				ServiceVersion = $"{faker.Random.Int(1, 3)}.{faker.Random.Int(0, 12)}.{faker.Random.Int(0, 99)}",
				Environment = faker.PickRandom(Environments),
				HostName = faker.PickRandom(HostNames),
				HostIp = faker.Internet.IpAddress().ToString(),
				TraceId = traceId,
				SpanId = traceId != null ? faker.Random.Hexadecimal(16, "") : null,
				TransactionId = traceId != null ? faker.Random.Guid().ToString("N")[..16] : null,
				UserId = faker.Random.Bool(0.6f) ? faker.Random.Guid().ToString("N") : null,
				HttpMethod = httpMethod,
				UrlPath = httpMethod != null ? faker.PickRandom(UrlPaths) : null,
				HttpStatusCode = httpMethod != null
					? level >= LogLevel.Error
						? faker.PickRandom(400, 401, 403, 404, 500, 502, 503)
						: faker.PickRandom(200, 201, 204)
					: null,
				DurationNanos = httpMethod != null ? faker.Random.Long(1_000_000, 5_000_000_000) : null,
				Labels = faker.Random.Bool(0.6f) ? new LogLabels
				{
					OrderId = orderIds != null && faker.Random.Bool(0.3f) ? orderIds[faker.Random.Int(0, orderIds.Count - 1)] : null,
					ProductId = productIds != null && faker.Random.Bool(0.2f) ? productIds[faker.Random.Int(0, productIds.Count - 1)] : null,
					CustomerId = customerIds != null && faker.Random.Bool(0.25f) ? customerIds[faker.Random.Int(0, customerIds.Count - 1)] : null,
					Action = faker.Random.Bool(0.5f) ? faker.PickRandom(Actions) : null
				} : null
			});
		}

		return logs;
	}
}
