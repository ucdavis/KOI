namespace Koi.Functions.Financial.Configuration;

public sealed class FinancialOptions
{
    public const string SectionName = "Financial";

    public string ApiUrl { get; init; } = string.Empty;

    public string ConsumerKey { get; init; } = string.Empty;

    public string ConsumerSecret { get; init; } = string.Empty;

    public string TokenEndpoint { get; init; } = string.Empty;

    public string ScopeApp { get; init; } = string.Empty;

    public string ScopeEnv { get; init; } = string.Empty;

    public static bool IsValid(FinancialOptions options)
    {
        return IsAbsoluteHttpUri(options.ApiUrl)
            && !string.IsNullOrWhiteSpace(options.ConsumerKey)
            && !string.IsNullOrWhiteSpace(options.ConsumerSecret)
            && IsAbsoluteHttpUri(options.TokenEndpoint)
            && !string.IsNullOrWhiteSpace(options.ScopeApp)
            && !string.IsNullOrWhiteSpace(options.ScopeEnv);
    }

    private static bool IsAbsoluteHttpUri(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
