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
        Assert.Equal("0.1.2", response.Version);
        Assert.NotEmpty(response.Revision);
    }

    [Fact]
    public void HelloContractIsStable()
    {
        var response = new HelloResponse("Hello from KOI");

        Assert.Equal("Hello from KOI", response.Message);
    }

    [Fact]
    public void FinancialFullDetailsContractIsStable()
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

    [Fact]
    public void FinancialDetailsMapsMissingOptionalValuesToEmptyStrings()
    {
        var response = FinancialDetails.FromAeDetails(new AeDetails
        {
            IsValid = false,
            ChartType = "INVALID",
            ChartString = "invalid",
            Errors = ["Invalid Chart Type"],
            Warnings = ["Example warning"],
            FundPurpose = null
        });

        Assert.False(response.IsValid);
        Assert.Equal("This is not a valid chart string.", response.ValidationStatus);
        Assert.Equal("INVALID", response.ChartType);
        Assert.Equal("invalid", response.ChartString);
        Assert.Equal("Invalid Chart Type", response.Error);
        Assert.Equal("Example warning", response.Warning);
        Assert.Equal(string.Empty, response.GlFinancialDepartmentName);
        Assert.Equal(string.Empty, response.ProjectStartDate);
        Assert.Equal(string.Empty, response.ProjectCompletionDate);
        Assert.Equal(string.Empty, response.AwardStatus);
        Assert.Equal(string.Empty, response.AwardStartDate);
        Assert.Equal(string.Empty, response.AwardEndDate);
        Assert.Equal(string.Empty, response.AwardInfo);
        Assert.Equal(string.Empty, response.ProjectTypeName);
        Assert.Equal(string.Empty, response.PrincipalInvestigatorName);
        Assert.Equal(string.Empty, response.PrincipalInvestigatorEmail);
        Assert.Equal(string.Empty, response.ProjectManagerName);
        Assert.Equal(string.Empty, response.ProjectManagerEmail);
        Assert.Equal(string.Empty, response.FundPurpose);
    }

    [Theory]
    [InlineData(
        true,
        FinancialChartStringType.Gl,
        "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000")]
    [InlineData(
        true,
        FinancialChartStringType.Ppm,
        "0000000000-000000-0000000-000000")]
    [InlineData(false, FinancialChartStringType.Invalid, "invalid")]
    public void FinancialDetailsValidationStatusMatchesValidationMessage(
        bool isValid,
        FinancialChartStringType chartStringType,
        string chartString)
    {
        var details = FinancialDetails.FromAeDetails(new AeDetails
        {
            IsValid = isValid,
            ChartStringType = chartStringType
        });
        var validation = new FinancialValidationResult
        {
            IsValid = isValid,
            ChartString = chartString
        };

        Assert.Equal(validation.Message, details.ValidationStatus);
    }

    [Theory]
    [InlineData(true, FinancialChartStringType.Gl, "This is a valid GL chart string.")]
    [InlineData(true, FinancialChartStringType.Ppm, "This is a valid PPM chart string.")]
    [InlineData(false, FinancialChartStringType.Gl, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Ppm, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Invalid, "This is not a valid chart string.")]
    public void FinancialFullDetailsMessageMatchesValidationResult(
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
        const string chartString =
            "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000";
        var response = new FinancialValidationResult
        {
            ChartString = chartString,
            IsValid = true,
            IsWarning = true,
            ErrorMessage = "example warning"
        };

        Assert.Equal(chartString, response.ChartString);
        Assert.Equal("GL", response.ChartType);
        Assert.True(response.IsValid);
        Assert.Equal("This is a valid GL chart string.", response.Message);
        Assert.True(response.IsWarning);
        Assert.Equal("example warning", response.ErrorMessage);
    }

    [Theory]
    [InlineData(
        true,
        "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000",
        "GL",
        "This is a valid GL chart string.")]
    [InlineData(
        true,
        "0000000000-000000-0000000-000000",
        "PPM",
        "This is a valid PPM chart string.")]
    [InlineData(
        false,
        "0000-00000-0000000-000000-00-000-0000000000-000000-0000-000000-000000",
        "GL",
        "This is not a valid chart string.")]
    [InlineData(
        false,
        "0000000000-000000-0000000-000000",
        "PPM",
        "This is not a valid chart string.")]
    [InlineData(false, "invalid", "INVALID", "This is not a valid chart string.")]
    public void FinancialValidationMessageMatchesValidationResult(
        bool isValid,
        string chartString,
        string expectedChartType,
        string expectedMessage)
    {
        var response = new FinancialValidationResult
        {
            IsValid = isValid,
            ChartString = chartString
        };

        Assert.Equal(expectedChartType, response.ChartType);
        Assert.Equal(expectedMessage, response.Message);
    }
}
