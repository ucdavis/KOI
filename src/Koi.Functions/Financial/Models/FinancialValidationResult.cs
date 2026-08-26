namespace Koi.Functions.Financial.Models;

public sealed class FinancialValidationResult
{
    public string ChartString { get; set; } = string.Empty;

    public bool IsValid { get; set; }

    public bool IsWarning { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;
}
