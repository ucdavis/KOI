using Koi.Functions.Authentication;
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
    public void UnknownFutureFunctionIsAuthenticatedByDefault()
    {
        Assert.False(_policy.IsAnonymous("FutureFunction"));
    }
}
