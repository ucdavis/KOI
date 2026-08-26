namespace Koi.Functions.Financial.Models;

public sealed class PpmDetails
{
    public string PpmGlString { get; set; } = string.Empty;

    public string? ProjectStartDate { get; set; } = string.Empty;

    public string? ProjectCompletionDate { get; set; } = string.Empty;

    public string? ProjectStatus { get; set; } = string.Empty;

    public string? AwardStatus { get; set; } = string.Empty;

    public string? AwardStartDate { get; set; } = string.Empty;

    public string? AwardEndDate { get; set; } = string.Empty;

    public string? AwardCloseDate { get; set; } = string.Empty;

    public string? AwardInfo { get; set; } = string.Empty;

    public string? ProjectTypeName { get; set; } = string.Empty;

    public string PoetString { get; set; } = string.Empty;

    public string? GlRevenueTransferString { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; } = string.Empty;

    public string? TaskStartDate { get; set; } = string.Empty;

    public string? TaskEndDate { get; set; } = string.Empty;

    public List<PpmRoles> Roles { get; set; } = [];
}
