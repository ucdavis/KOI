namespace Koi.Functions.Financial.Models;

public sealed class SegmentDetails
{
    public int Order { get; set; }

    public string? Entity { get; set; } = string.Empty;

    public string? Code { get; set; } = string.Empty;

    public string? Name { get; set; } = string.Empty;

    public bool GiftFund { get; set; }

    public bool EndowmentGiftFund { get; set; }
}
