// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Playground.Models;

namespace Playground.Helpers;

public static class SampleDataGenerator
{
	private static readonly string[] Categories = ["Hardware", "Software", "Network", "Security", "Account"];

	private static readonly string[] Subjects =
	[
		"Cannot connect to VPN",
		"Password reset required",
		"Laptop screen flickering",
		"Email not syncing",
		"Printer not working",
		"Software installation request",
		"Network drive inaccessible",
		"Security alert investigation",
		"New account setup",
		"Application crash on startup"
	];

	private static readonly string[] Users = ["alice@company.com", "bob@company.com", "charlie@company.com"];
	private static readonly string?[] Agents = ["support-agent-1", "support-agent-2", "support-agent-3", null];

	public static List<SupportTicket> GenerateTickets(int count = 100, int seed = 42)
	{
		var random = new Random(seed); // Fixed seed for reproducibility
		var tickets = new List<SupportTicket>();

		for (var i = 1; i <= count; i++)
		{
			var createdAt = DateTime.UtcNow.AddDays(-random.Next(0, 30)).AddHours(-random.Next(0, 24));
			var status = (TicketStatus)random.Next(0, 4);
			var resolvedAt = status == TicketStatus.Resolved
				? createdAt.AddHours(random.Next(1, 72))
				: (DateTime?)null;

			tickets.Add(new SupportTicket
			{
				TicketId = $"TKT-{i:D5}",
				Subject = Subjects[random.Next(Subjects.Length)],
				Description = $"Detailed description for ticket {i}. User reports issues with {Categories[random.Next(Categories.Length)].ToLowerInvariant()} systems.",
				Status = status,
				Priority = (TicketPriority)random.Next(0, 4),
				Category = Categories[random.Next(Categories.Length)],
				CreatedAt = createdAt,
				UpdatedAt = createdAt.AddHours(random.Next(1, 48)),
				ResolvedAt = resolvedAt,
				ReportedBy = Users[random.Next(Users.Length)],
				AssignedTo = Agents[random.Next(Agents.Length)],
				IsEscalated = random.NextDouble() < 0.1,
				Tags = ["support", Categories[random.Next(Categories.Length)].ToLowerInvariant()],
				Metadata = new TicketMetadata
				{
					Source = random.NextDouble() < 0.5 ? "web-portal" : "email",
					Browser = "Chrome/120.0",
					OperatingSystem = random.NextDouble() < 0.7 ? "Windows 11" : "macOS 14"
				},
				Responses =
				[
					new TicketResponse
					{
						ResponseId = $"RSP-{i}-1",
						Author = Agents[random.Next(Agents.Length - 1)]!, // Exclude null
						Content = "Thank you for reporting this issue. We are investigating.",
						CreatedAt = createdAt.AddMinutes(random.Next(5, 120)),
						IsInternal = false
					}
				]
			});
		}

		return tickets;
	}
}
