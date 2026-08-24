using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Koi.Functions.Hello;

public sealed class HelloFunction
{
    public const string FunctionName = "Hello";

    [Function(FunctionName)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/hello")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(
            new HelloResponse("Hello from KOI"),
            cancellationToken);
        return response;
    }
}

public sealed record HelloResponse(string Message);
