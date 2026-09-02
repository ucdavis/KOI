using System.Net;
using Koi.Functions.Financial.Models;
using Koi.Functions.Financial.Services;
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

    [Function(FunctionName)]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/financial/details/{value}")] HttpRequestData request,
        string value,
        CancellationToken cancellationToken)
    {
        var aeDetails = await _aggieEnterpriseService.GetAeDetailsAsync(value, cancellationToken);
        var financialDetails = FinancialDetails.FromAeDetails(aeDetails);
        var response = request.CreateResponse();
        response.StatusCode = HttpStatusCode.OK;
        await response.WriteAsJsonAsync(financialDetails, cancellationToken);
        return response;
    }
}
