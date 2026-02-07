// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Transport;

namespace Playground.Helpers;

public static class ConnectionHelper
{
	public static async Task<bool> VerifyConnectionAsync(ITransport transport)
	{
		Console.WriteLine("Verifying connection...");

		var endpointPath = new EndpointPath(Elastic.Transport.HttpMethod.HEAD, "/");
		var response = await transport.RequestAsync<VoidResponse>(in endpointPath);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
		{
			Console.WriteLine($"Failed to connect: {response.ApiCallDetails.DebugInformation}");
			return false;
		}

		Console.WriteLine("Connected!\n");
		return true;
	}
}
