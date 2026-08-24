using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Koi.Functions.Authentication;

public sealed class ApiKeyAuthenticator(IOptions<ApiKeyOptions> options)
{
    private readonly ApiKeyOptions _options = options.Value;

    public ApiKeyAuthenticationResult Authenticate(IEnumerable<string>? authorizationHeaders)
    {
        if (!TryGetSingleHeader(authorizationHeaders, out var authorizationHeader)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return ApiKeyAuthenticationResult.Failed;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(header.Parameter));
        string? matchedKeyId = null;

        foreach (var credential in _options.Credentials.Where(credential => credential.Enabled))
        {
            if (!ApiKeyHash.TryDecode(credential.Sha256, out var configuredHash))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(presentedHash, configuredHash))
            {
                matchedKeyId = credential.Id;
            }
        }

        return matchedKeyId is null
            ? ApiKeyAuthenticationResult.Failed
            : new ApiKeyAuthenticationResult(true, matchedKeyId);
    }

    private static bool TryGetSingleHeader(
        IEnumerable<string>? authorizationHeaders,
        out string authorizationHeader)
    {
        authorizationHeader = string.Empty;
        if (authorizationHeaders is null)
        {
            return false;
        }

        using var enumerator = authorizationHeaders.GetEnumerator();
        if (!enumerator.MoveNext() || string.IsNullOrWhiteSpace(enumerator.Current))
        {
            return false;
        }

        authorizationHeader = enumerator.Current;
        return !enumerator.MoveNext();
    }
}

public sealed record ApiKeyAuthenticationResult(bool IsAuthenticated, string? KeyId)
{
    public static ApiKeyAuthenticationResult Failed { get; } = new(false, null);
}

internal static class ApiKeyHash
{
    public static bool TryDecode(string? value, out byte[] hash)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            hash = [];
            return false;
        }

        try
        {
            hash = Convert.FromHexString(value);
            return hash.Length == SHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            hash = [];
            return false;
        }
    }
}
