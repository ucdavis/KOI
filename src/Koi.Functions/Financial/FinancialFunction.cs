using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Koi.Functions.Financial;

public sealed class FinancialFunction
{
    public const string FunctionName = "Financial";

    [Function(FunctionName)]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/financial/{value}")] HttpRequestData request,
        string value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(
            new FinancialResponse($"You passed: {value}"),
            cancellationToken);
        return response;
    }
}

public sealed record FinancialResponse(string Message);
