using Koi.Functions.Health;
using Koi.Functions.Hello;

namespace Koi.Functions.Tests;

public sealed class ResponseContractTests
{
    [Fact]
    public void HealthContractIsStable()
    {
        var response = new HealthResponse(
            "healthy",
            ServiceMetadata.Name,
            ServiceMetadata.Version,
            ServiceMetadata.Revision);

        Assert.Equal("healthy", response.Status);
        Assert.Equal("KOI", response.Service);
        Assert.Equal("0.1.0", response.Version);
        Assert.NotEmpty(response.Revision);
    }

    [Fact]
    public void HelloContractIsStable()
    {
        var response = new HelloResponse("Hello from KOI");

        Assert.Equal("Hello from KOI", response.Message);
    }
}
