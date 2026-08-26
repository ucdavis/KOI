namespace Koi.Functions.Financial.Models;

public sealed class Approver
{
    public string? FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; } = string.Empty;

    public string? Email { get; set; } = string.Empty;

    public string? FullName { get; set; } = string.Empty;

    public string Name => !string.IsNullOrWhiteSpace(FullName)
        ? FullName
        : $"{LastName}, {FirstName}";
}
