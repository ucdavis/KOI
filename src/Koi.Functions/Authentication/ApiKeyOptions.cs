namespace Koi.Functions.Authentication;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    public List<ApiKeyCredential> Credentials { get; init; } = [];

    public static bool IsValid(ApiKeyOptions options)
    {
        if (options.Credentials.Count == 0)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var credential in options.Credentials)
        {
            if (!IsValidId(credential.Id)
                || !ids.Add(credential.Id)
                || !ApiKeyHash.TryDecode(credential.Sha256, out _)
                || !hashes.Add(credential.Sha256))
            {
                return false;
            }
        }

        return options.Credentials.Any(credential => credential.Enabled);
    }

    private static bool IsValidId(string? id)
    {
        return id is { Length: > 0 and <= 64 }
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}

public sealed class ApiKeyCredential
{
    public required string Id { get; init; }

    public required string Sha256 { get; init; }

    public bool Enabled { get; init; }
}
