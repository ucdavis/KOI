namespace Koi.Functions.Financial.Models;

public sealed class FinancialDetails
{
    private const string GlFinancialDepartmentEntity = "GL Financial Department";
    private const string AwardEntity = "Award";
    private const string PrincipalInvestigatorRole = "Principal Investigator";
    private const string ProjectManagerRole = "Project Manager";
    private const string ProjectRoleType = "P";

    public bool IsValid { get; set; }

    public string ValidationStatus { get; set; } = string.Empty;

    public string ChartType { get; set; } = string.Empty;

    public string ChartString { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public string Warning { get; set; } = string.Empty;

    public string GlFinancialDepartmentName { get; set; } = string.Empty;

    public string ProjectStartDate { get; set; } = string.Empty;

    public string ProjectCompletionDate { get; set; } = string.Empty;

    public string AwardStatus { get; set; } = string.Empty;

    public string AwardStartDate { get; set; } = string.Empty;

    public string AwardEndDate { get; set; } = string.Empty;

    public string AwardInfo { get; set; } = string.Empty;

    public string ProjectTypeName { get; set; } = string.Empty;

    public string PrincipalInvestigatorName { get; set; } = string.Empty;

    public string PrincipalInvestigatorEmail { get; set; } = string.Empty;

    public string ProjectManagerName { get; set; } = string.Empty;

    public string ProjectManagerEmail { get; set; } = string.Empty;

    public string FundPurpose { get; set; } = string.Empty;

    public static FinancialDetails FromAeDetails(AeDetails aeDetails)
    {
        ArgumentNullException.ThrowIfNull(aeDetails);

        var principalInvestigator = FindProjectRoleApprover(
            aeDetails,
            PrincipalInvestigatorRole);
        var projectManager = FindProjectRoleApprover(aeDetails, ProjectManagerRole);

        return new FinancialDetails
        {
            IsValid = aeDetails.IsValid,
            ValidationStatus = aeDetails.Message,
            ChartType = aeDetails.ChartType,
            ChartString = aeDetails.ChartString,
            Error = aeDetails.Error,
            Warning = aeDetails.Warning,
            GlFinancialDepartmentName = FindSegmentName(
                aeDetails,
                GlFinancialDepartmentEntity),
            ProjectStartDate = aeDetails.PpmDetails?.ProjectStartDate ?? string.Empty,
            ProjectCompletionDate = aeDetails.PpmDetails?.ProjectCompletionDate ?? string.Empty,
            AwardStatus = aeDetails.PpmDetails?.AwardStatus ?? string.Empty,
            AwardStartDate = aeDetails.PpmDetails?.AwardStartDate ?? string.Empty,
            AwardEndDate = aeDetails.PpmDetails?.AwardEndDate ?? string.Empty,
            AwardInfo = FindSegmentName(aeDetails, AwardEntity),
            ProjectTypeName = aeDetails.PpmDetails?.ProjectTypeName ?? string.Empty,
            PrincipalInvestigatorName = principalInvestigator?.Name ?? string.Empty,
            PrincipalInvestigatorEmail = principalInvestigator?.Email ?? string.Empty,
            ProjectManagerName = projectManager?.Name ?? string.Empty,
            ProjectManagerEmail = projectManager?.Email ?? string.Empty,
            FundPurpose = aeDetails.FundPurpose ?? string.Empty
        };
    }

    private static string FindSegmentName(AeDetails aeDetails, string entity)
    {
        return aeDetails.SegmentDetails
            .FirstOrDefault(segment => string.Equals(
                segment.Entity,
                entity,
                StringComparison.Ordinal))
            ?.Name ?? string.Empty;
    }

    private static Approver? FindProjectRoleApprover(AeDetails aeDetails, string roleName)
    {
        var role = aeDetails.PpmDetails?.Roles
            .Where(role => string.Equals(role.Type, ProjectRoleType, StringComparison.Ordinal))
            .Where(role => string.Equals(
                role.RoleName,
                roleName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(role => role.Order)
            .FirstOrDefault();

        return role?.Approvers.FirstOrDefault();
    }
}
