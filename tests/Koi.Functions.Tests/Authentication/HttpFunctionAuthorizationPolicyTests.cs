using Koi.Functions.Authentication;
using Koi.Functions.Financial;
using Koi.Functions.Health;
using Koi.Functions.Hello;

namespace Koi.Functions.Tests.Authentication;

public sealed class HttpFunctionAuthorizationPolicyTests
{
    private readonly HttpFunctionAuthorizationPolicy _policy = new();

    [Fact]
    public void HealthIsExplicitlyAnonymous()
    {
        Assert.True(_policy.IsAnonymous(HealthFunction.FunctionName));
    }

    [Fact]
    public void HelloIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(HelloFunction.FunctionName));
    }

    [Fact]
    public void FinancialIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.FunctionName));
    }

    [Fact]
    public void FinancialBulkIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.BulkFunctionName));
    }

    [Fact]
    public void FinancialFullDetailsIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.FullDetailsFunctionName));
    }

    [Fact]
    public void FinancialBulkFullDetailsIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.BulkFullDetailsFunctionName));
    }

    [Fact]
    public void FinancialValidationIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.ValidationFunctionName));
    }

    [Fact]
    public void FinancialValidationBulkIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous(FinancialFunction.BulkValidationFunctionName));
    }

    [Fact]
    public void UnknownFutureFunctionIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous("FutureFunction"));
    }
}
