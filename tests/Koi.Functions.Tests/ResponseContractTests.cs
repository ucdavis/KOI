using AggieEnterpriseApi.Validation;
using Koi.Functions.Financial.Models;
using Koi.Functions.Health;
using Koi.Functions.Hello;

namespace Koi.Functions.Tests;

public sealed class ResponseContractTests
{
    [Fact]
    public void HealthContractIsStable()
    {
        var response = new HealthResponse(
            "healthy",
            ServiceMetadata.Name,
            ServiceMetadata.Version,
            ServiceMetadata.Revision);

        Assert.Equal("healthy", response.Status);
        Assert.Equal("KOI", response.Service);
        Assert.Equal("0.1.1", response.Version);
        Assert.NotEmpty(response.Revision);
    }

    [Fact]
    public void HelloContractIsStable()
    {
        var response = new HelloResponse("Hello from KOI");

        Assert.Equal("Hello from KOI", response.Message);
    }

    [Fact]
    public void FinancialDetailsContractIsStable()
    {
        var response = new AeDetails
        {
            IsValid = true,
            ChartType = "GL",
            ChartString = "example",
            ChartStringType = FinancialChartStringType.Gl
        };

        Assert.True(response.IsValid);
        Assert.Equal("GL", response.ChartType);
        Assert.Equal("example", response.ChartString);
        Assert.Equal(FinancialChartStringType.Gl, response.ChartStringType);
    }
}
