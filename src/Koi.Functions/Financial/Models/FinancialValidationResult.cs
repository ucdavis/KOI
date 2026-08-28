using AggieEnterpriseApi.Validation;

namespace Koi.Functions.Financial.Models;

public sealed class FinancialValidationResult
{
    private const string InvalidChartStringMessage = "This is not a valid chart string.";

    private FinancialChartStringType ChartStringType => string.IsNullOrWhiteSpace(ChartString)
        ? FinancialChartStringType.Invalid
        : FinancialChartValidation.GetFinancialChartStringType(ChartString);

    public string ChartString { get; set; } = string.Empty;

    public string ChartType => ChartStringType.ToString().ToUpperInvariant();

    public bool IsValid { get; set; }

    public string Message => IsValid
        ? ChartStringType switch
        {
            FinancialChartStringType.Gl => "This is a valid GL chart string.",
            FinancialChartStringType.Ppm => "This is a valid PPM chart string.",
            _ => InvalidChartStringMessage
        }
        : InvalidChartStringMessage;

    public bool IsWarning { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}
