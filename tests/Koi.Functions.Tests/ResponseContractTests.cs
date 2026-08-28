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
        Assert.Equal("This is a valid GL chart string.", response.Message);
        Assert.Equal("example", response.ChartString);
        Assert.Equal(FinancialChartStringType.Gl, response.ChartStringType);
    }

    [Theory]
    [InlineData(true, FinancialChartStringType.Gl, "This is a valid GL chart string.")]
    [InlineData(true, FinancialChartStringType.Ppm, "This is a valid PPM chart string.")]
    [InlineData(false, FinancialChartStringType.Gl, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Ppm, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Invalid, "This is not a valid chart string.")]
    public void FinancialDetailsMessageMatchesValidationResult(
        bool isValid,
        FinancialChartStringType chartStringType,
        string expectedMessage)
    {
        var response = new AeDetails
        {
            IsValid = isValid,
            ChartStringType = chartStringType
        };

        Assert.Equal(expectedMessage, response.Message);
    }

    [Fact]
    public void FinancialValidationContractIsStable()
    {
        var response = new FinancialValidationResult
        {
            ChartString = "example",
            IsValid = true,
            IsWarning = true,
            ErrorMessage = "example warning"
        };

        Assert.Equal("example", response.ChartString);
        Assert.True(response.IsValid);
        Assert.True(response.IsWarning);
        Assert.Equal("example warning", response.ErrorMessage);
    }
}
