using System.Net;
using System.Text.Json;
using Koi.Functions.Financial.Models;
using Koi.Functions.Financial.Services;
using Koi.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Koi.Functions.Financial;

public sealed class FinancialFunction
{
    internal const int MaxBatchSize = 50;
    internal const int MaxConcurrency = 5;

    private readonly IAggieEnterpriseService _aggieEnterpriseService;

    public FinancialFunction(IAggieEnterpriseService aggieEnterpriseService)
    {
        _aggieEnterpriseService = aggieEnterpriseService;
    }

    public const string FunctionName = "Financial";
    public const string BulkFunctionName = "FinancialBulk";
    public const string ValidationFunctionName = "FinancialValidation";
    public const string BulkValidationFunctionName = "FinancialValidationBulk";

    [Function(FunctionName)]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/financial/details/{value}")] HttpRequestData request,
        string value,
        CancellationToken cancellationToken)
    {
        var aeDetails = await _aggieEnterpriseService.GetAeDetailsAsync(value, cancellationToken);
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(aeDetails, cancellationToken);
        return response;
    }

    [Function(BulkFunctionName)]
    public async Task<HttpResponseData> RunBulk(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/financial/details")] HttpRequestData request,
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

        if (chartStrings.Length > MaxBatchSize)
        {
            return await CreateBadRequestResponseAsync(
                request,
                $"request body must contain no more than {MaxBatchSize} chart strings",
                cancellationToken);
        }

        var aeDetails = await GetAeDetailsAsync(chartStrings, cancellationToken);

        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(aeDetails, cancellationToken);
        return response;
    }

    [Function(ValidationFunctionName)]
    public async Task<HttpResponseData> RunValidation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/financial/validate/{value}")] HttpRequestData request,
        string value,
        CancellationToken cancellationToken)
    {
        var result = await _aggieEnterpriseService.ValidateAsync(value, cancellationToken);
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }

    [Function(BulkValidationFunctionName)]
    public async Task<HttpResponseData> RunBulkValidation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/financial/validate")] HttpRequestData request,
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

        if (chartStrings.Length > MaxBatchSize)
        {
            return await CreateBadRequestResponseAsync(
                request,
                $"request body must contain no more than {MaxBatchSize} chart strings",
                cancellationToken);
        }

        var validationResults = await ValidateAsync(chartStrings, cancellationToken);

        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(validationResults, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> CreateInvalidBodyResponseAsync(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        return await CreateBadRequestResponseAsync(
            request,
            "request body must be a JSON array of chart strings",
            cancellationToken);
    }

    private async Task<AeDetails[]> GetAeDetailsAsync(
        string[] chartStrings,
        CancellationToken cancellationToken)
    {
        var results = new AeDetails[chartStrings.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, chartStrings.Length),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxConcurrency
            },
            async (index, iterationCancellationToken) =>
            {
                results[index] = await _aggieEnterpriseService.GetAeDetailsAsync(
                    chartStrings[index],
                    iterationCancellationToken);
            });

        return results;
    }

    private async Task<FinancialValidationResult[]> ValidateAsync(
        string[] chartStrings,
        CancellationToken cancellationToken)
    {
        var results = new FinancialValidationResult[chartStrings.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, chartStrings.Length),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxConcurrency
            },
            async (index, iterationCancellationToken) =>
            {
                results[index] = await _aggieEnterpriseService.ValidateAsync(
                    chartStrings[index],
                    iterationCancellationToken);
            });

        return results;
    }

    private static async Task<HttpResponseData> CreateBadRequestResponseAsync(
        HttpRequestData request,
        string error,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.BadRequest;
        await response.WriteAsJsonAsync(
            new ErrorResponse(error),
            cancellationToken);
        return response;
    }
}
