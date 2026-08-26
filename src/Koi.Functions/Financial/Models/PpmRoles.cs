namespace Koi.Functions.Financial.Models;

public sealed class PpmRoles
{
    public string RoleName { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Type { get; set; } = string.Empty;

    public List<Approver> Approvers { get; set; } = [];
}
