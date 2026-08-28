using AggieEnterpriseApi.Validation;

namespace Koi.Functions.Financial.Models;

public sealed class AeDetails
{
    private const string InvalidChartStringMessage = "This is not a valid chart string.";

    public bool IsValid { get; set; } = true;

    public string ChartType { get; set; } = string.Empty;

    public string Message => IsValid
        ? ChartStringType switch
        {
            FinancialChartStringType.Gl => "This is a valid GL chart string.",
            FinancialChartStringType.Ppm => "This is a valid PPM chart string.",
            _ => InvalidChartStringMessage
        }
        : InvalidChartStringMessage;

    public string ChartString { get; set; } = string.Empty;

    public FinancialChartStringType ChartStringType { get; set; } = FinancialChartStringType.Invalid;

    public string Error => Errors.Count == 0 ? string.Empty : string.Join(" ", Errors);

    public string Warning => Warnings.Count == 0 ? string.Empty : string.Join(" ", Warnings);

    public List<string> Errors { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public List<SegmentDetails> SegmentDetails { get; set; } = [];

    public List<Approver> Approvers { get; set; } = [];

    public PpmDetails? PpmDetails { get; set; }

    public string? FundPurpose { get; set; } = string.Empty;

    public bool HasWarnings => Warnings.Count > 0;
}
