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
        Assert.Equal("0.1.4", response.Version);
        Assert.NotEmpty(response.Revision);
    }

    [Fact]
    public void HelloContractIsStable()
    {
        var response = new HelloResponse("Hello from KOI");

        Assert.Equal("Hello from KOI", response.Message);
    }

    [Fact]
    public void AeDetailsContractIsStable()
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
        Assert.Equal(string.Empty, response.EntityName);
        Assert.Equal(string.Empty, response.FundName);
        Assert.Equal(string.Empty, response.DepartmentName);
        Assert.Equal(string.Empty, response.AccountName);
        Assert.Equal(string.Empty, response.PurposeName);
        Assert.Equal(string.Empty, response.ProgramName);
        Assert.Equal(string.Empty, response.ProjectName);
        Assert.Equal(string.Empty, response.ActivityName);
        Assert.Equal(string.Empty, response.TaskName);
        Assert.Equal(string.Empty, response.ExpenditureOrganizationName);
        Assert.Equal(string.Empty, response.ExpenditureTypeName);
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

    [Fact]
    public void FinancialDetailsMapsGlSegmentNamesFromAeDetails()
    {
        var response = FinancialDetails.FromAeDetails(new AeDetails
        {
            ChartStringType = FinancialChartStringType.Gl,
            SegmentDetails =
            [
                new SegmentDetails { Entity = "Entity", Name = "UC Davis" },
                new SegmentDetails { Entity = "Fund", Name = "General Funds" },
                new SegmentDetails { Entity = "Department", Name = "Computer Science" },
                new SegmentDetails { Entity = "Account", Name = "Supplies" },
                new SegmentDetails { Entity = "Purpose", Name = "Instruction" },
                new SegmentDetails { Entity = "Program", Name = "Academic Programs" },
                new SegmentDetails { Entity = "Project", Name = "Campus Project" },
                new SegmentDetails { Entity = "Activity", Name = "Core Activity" }
            ]
        });

        Assert.Equal("UC Davis", response.EntityName);
        Assert.Equal("General Funds", response.FundName);
        Assert.Equal("Computer Science", response.DepartmentName);
        Assert.Equal("Supplies", response.AccountName);
        Assert.Equal("Instruction", response.PurposeName);
        Assert.Equal("Academic Programs", response.ProgramName);
        Assert.Equal("Campus Project", response.ProjectName);
        Assert.Equal("Core Activity", response.ActivityName);
        Assert.Equal(string.Empty, response.TaskName);
        Assert.Equal(string.Empty, response.ExpenditureOrganizationName);
        Assert.Equal(string.Empty, response.ExpenditureTypeName);
    }

    [Fact]
    public void FinancialDetailsMapsPpmSegmentNamesFromAeDetails()
    {
        var response = FinancialDetails.FromAeDetails(new AeDetails
        {
            ChartStringType = FinancialChartStringType.Ppm,
            SegmentDetails =
            [
                new SegmentDetails { Entity = "Project", Name = "Research Project" },
                new SegmentDetails { Entity = "Task", Name = "Project Task" },
                new SegmentDetails
                {
                    Entity = "Expenditure Organization",
                    Name = "Engineering"
                },
                new SegmentDetails
                {
                    Entity = "Expenditure Type",
                    Name = "Research Supplies"
                }
            ]
        });

        Assert.Equal("Research Project", response.ProjectName);
        Assert.Equal("Project Task", response.TaskName);
        Assert.Equal("Engineering", response.ExpenditureOrganizationName);
        Assert.Equal("Research Supplies", response.ExpenditureTypeName);
        Assert.Equal(string.Empty, response.EntityName);
        Assert.Equal(string.Empty, response.FundName);
        Assert.Equal(string.Empty, response.DepartmentName);
        Assert.Equal(string.Empty, response.AccountName);
        Assert.Equal(string.Empty, response.PurposeName);
        Assert.Equal(string.Empty, response.ProgramName);
        Assert.Equal(string.Empty, response.ActivityName);
    }

    [Theory]
    [InlineData(
        true,
        FinancialChartStringType.Gl,
        "This is a valid GL chart string.")]
    [InlineData(
        true,
        FinancialChartStringType.Ppm,
        "This is a valid PPM chart string.")]
    [InlineData(
        false,
        FinancialChartStringType.Invalid,
        "This is not a valid chart string.")]
    public void FinancialDetailsValidationStatusMatchesAeDetailsMessage(
        bool isValid,
        FinancialChartStringType chartStringType,
        string expectedMessage)
    {
        var aeDetails = new AeDetails
        {
            IsValid = isValid,
            ChartStringType = chartStringType
        };
        var details = FinancialDetails.FromAeDetails(aeDetails);

        Assert.Equal(expectedMessage, aeDetails.Message);
        Assert.Equal(aeDetails.Message, details.ValidationStatus);
    }

    [Theory]
    [InlineData(true, FinancialChartStringType.Gl, "This is a valid GL chart string.")]
    [InlineData(true, FinancialChartStringType.Ppm, "This is a valid PPM chart string.")]
    [InlineData(false, FinancialChartStringType.Gl, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Ppm, "This is not a valid chart string.")]
    [InlineData(false, FinancialChartStringType.Invalid, "This is not a valid chart string.")]
    public void AeDetailsMessageMatchesValidationStatus(
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
    public void FinancialValidationResultContractIsStable()
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
    public void FinancialValidationResultDerivesChartTypeAndMessage(
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
