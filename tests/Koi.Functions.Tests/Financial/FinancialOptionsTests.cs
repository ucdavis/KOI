using Koi.Functions.Financial.Configuration;

namespace Koi.Functions.Tests.Financial;

public sealed class FinancialOptionsTests
{
    [Fact]
    public void CompleteOptionsAreValid()
    {
        var options = new FinancialOptions
        {
            ApiUrl = "https://financial.example.test/graphql",
            ConsumerKey = "consumer-key",
            ConsumerSecret = "consumer-secret",
            TokenEndpoint = "https://identity.example.test/oauth2/token",
            ScopeApp = "KOI",
            ScopeEnv = "Production"
        };

        Assert.True(FinancialOptions.IsValid(options));
    }

    [Fact]
    public void MissingOptionsAreInvalid()
    {
        Assert.False(FinancialOptions.IsValid(new FinancialOptions()));
    }

    [Fact]
    public void NonHttpEndpointsAreInvalid()
    {
        var options = new FinancialOptions
        {
            ApiUrl = "file:///financial/graphql",
            ConsumerKey = "consumer-key",
            ConsumerSecret = "consumer-secret",
            TokenEndpoint = "https://identity.example.test/oauth2/token",
            ScopeApp = "KOI",
            ScopeEnv = "Production"
        };

        Assert.False(FinancialOptions.IsValid(options));
    }
}
