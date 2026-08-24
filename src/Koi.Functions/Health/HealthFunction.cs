using System.Net;
using Koi.Functions.Authentication;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Koi.Functions.Health;

public sealed class HealthFunction
{
    public const string FunctionName = "Health";

    [Function(FunctionName)]
    [AllowAnonymous]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(
            new HealthResponse("healthy", ServiceMetadata.Name, ServiceMetadata.Version),
            cancellationToken);
        return response;
    }
}

public sealed record HealthResponse(string Status, string Service, string Version);
