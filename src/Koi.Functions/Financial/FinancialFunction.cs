using System.Net;
using System.Text.Json;
using Koi.Functions.Financial.Services;
using Koi.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Koi.Functions.Financial;

public sealed class FinancialFunction
{
    private readonly IAggieEnterpriseService _aggieEnterpriseService;

    public FinancialFunction(IAggieEnterpriseService aggieEnterpriseService)
    {
        _aggieEnterpriseService = aggieEnterpriseService;
    }

    public const string FunctionName = "Financial";
    public const string BulkFunctionName = "FinancialBulk";

    [Function(FunctionName)]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/financial/{value}")] HttpRequestData request,
        string value,
        CancellationToken cancellationToken)
    {
        var aeDetails = await _aggieEnterpriseService.GetAeDetailsAsync(value);
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(aeDetails, cancellationToken);
        return response;
    }

    [Function(BulkFunctionName)]
    public async Task<HttpResponseData> RunBulk(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/financial")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        string[]? chartStrings;
        try
        {
            chartStrings = await JsonSerializer.DeserializeAsync<string[]>(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await CreateInvalidBodyResponseAsync(request, cancellationToken);
        }

        if (chartStrings is null)
        {
            return await CreateInvalidBodyResponseAsync(request, cancellationToken);
        }

        var aeDetails = await Task.WhenAll(
            chartStrings.Select(_aggieEnterpriseService.GetAeDetailsAsync));

        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(aeDetails, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> CreateInvalidBodyResponseAsync(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.BadRequest;
        await response.WriteAsJsonAsync(
            new ErrorResponse("request body must be a JSON array of chart strings"),
            cancellationToken);
        return response;
    }
}
