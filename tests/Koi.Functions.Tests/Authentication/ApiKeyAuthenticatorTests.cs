using System.Security.Cryptography;
using System.Text;
using Koi.Functions.Authentication;
using Microsoft.Extensions.Options;

namespace Koi.Functions.Tests.Authentication;

public sealed class ApiKeyAuthenticatorTests
{
    private const string PrimaryToken = "koi_test_primary.a-valid-test-token";
    private const string SecondaryToken = "koi_test_secondary.another-valid-test-token";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer incorrect")]
    public void MissingMalformedOrIncorrectCredentialsAreRejected(string? authorization)
    {
        var authenticator = CreateAuthenticator(Credential("primary", PrimaryToken));
        var headers = authorization is null ? null : new[] { authorization };

        var result = authenticator.Authenticate(headers);

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.KeyId);
    }

    [Fact]
    public void MultipleAuthorizationHeadersAreRejected()
    {
        var authenticator = CreateAuthenticator(Credential("primary", PrimaryToken));

        var result = authenticator.Authenticate(
            [$"Bearer {PrimaryToken}", $"Bearer {PrimaryToken}"]);

        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public void DisabledCredentialIsRejected()
    {
        var authenticator = CreateAuthenticator(Credential("primary", PrimaryToken, enabled: false));

        var result = authenticator.Authenticate([$"Bearer {PrimaryToken}"]);

        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public void BothActiveCredentialsAreAcceptedDuringRotation()
    {
        var authenticator = CreateAuthenticator(
            Credential("primary", PrimaryToken),
            Credential("secondary", SecondaryToken));

        var primary = authenticator.Authenticate([$"Bearer {PrimaryToken}"]);
        var secondary = authenticator.Authenticate([$"Bearer {SecondaryToken}"]);

        Assert.Equal(new ApiKeyAuthenticationResult(true, "primary"), primary);
        Assert.Equal(new ApiKeyAuthenticationResult(true, "secondary"), secondary);
    }

    [Fact]
    public void OptionsRejectDuplicateIdsAndHashes()
    {
        var duplicateIds = new ApiKeyOptions
        {
            Credentials =
            [
                Credential("duplicate", PrimaryToken),
                Credential("duplicate", SecondaryToken),
            ],
        };
        var duplicateHashes = new ApiKeyOptions
        {
            Credentials =
            [
                Credential("primary", PrimaryToken),
                Credential("secondary", PrimaryToken),
            ],
        };

        Assert.False(ApiKeyOptions.IsValid(duplicateIds));
        Assert.False(ApiKeyOptions.IsValid(duplicateHashes));
    }

    [Fact]
    public void OptionsRequireAtLeastOneEnabledCredential()
    {
        var options = new ApiKeyOptions
        {
            Credentials = [Credential("primary", PrimaryToken, enabled: false)],
        };

        Assert.False(ApiKeyOptions.IsValid(options));
    }

    private static ApiKeyAuthenticator CreateAuthenticator(params ApiKeyCredential[] credentials)
    {
        return new ApiKeyAuthenticator(Options.Create(new ApiKeyOptions { Credentials = [.. credentials] }));
    }

    private static ApiKeyCredential Credential(string id, string token, bool enabled = true)
    {
        return new ApiKeyCredential
        {
            Id = id,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            Enabled = enabled,
        };
    }
}
