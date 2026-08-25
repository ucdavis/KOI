using Koi.Functions.Authentication;
using Koi.Functions.Configuration;
using Microsoft.Extensions.Configuration;

namespace Koi.Functions.Tests.Configuration;

public sealed class LocalDevelopmentConfigurationTests
{
    [Fact]
    public void LoadsAzureShapedCredentialsAndClientTokensFromEnvFile()
    {
        var envFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                envFile,
                """
                ApiKeys__Credentials__0__Id=koi-local-primary
                ApiKeys__Credentials__0__Sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
                ApiKeys__Credentials__0__Enabled=true
                ApiKeys__Credentials__1__Id=koi-local-secondary
                ApiKeys__Credentials__1__Sha256=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
                ApiKeys__Credentials__1__Enabled=true
                KOI_API_KEY_1=local-client-token
                """);

            var configuration = new ConfigurationManager();

            LocalDevelopmentConfiguration.Add(configuration, envFile);

            var options = configuration
                .GetSection(ApiKeyOptions.SectionName)
                .Get<ApiKeyOptions>();

            Assert.NotNull(options);
            Assert.Collection(
                options.Credentials,
                credential =>
                {
                    Assert.Equal("koi-local-primary", credential.Id);
                    Assert.Equal(new string('a', 64), credential.Sha256);
                    Assert.True(credential.Enabled);
                },
                credential =>
                {
                    Assert.Equal("koi-local-secondary", credential.Id);
                    Assert.Equal(new string('b', 64), credential.Sha256);
                    Assert.True(credential.Enabled);
                });
            Assert.Equal("local-client-token", configuration["KOI_API_KEY_1"]);
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    [Fact]
    public void SuppliesRequiredLocalTelemetryAttributesAndPreservesCustomAttributes()
    {
        var envFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(
                envFile,
                """
                OTEL_EXPORTER_OTLP_ENDPOINT=https://collector.example.test
                OTEL_RESOURCE_ATTRIBUTES=service.name=wrong,custom.one=kept,deployment.environment=wrong,custom.two=also-kept
                OTEL_SERVICE_NAME=wrong
                """);

            var configuration = new ConfigurationManager();

            LocalDevelopmentConfiguration.Add(configuration, envFile);

            Assert.Equal("koi", configuration["OTEL_SERVICE_NAME"]);
            Assert.Equal(
                $"service.name=koi,service.version={ServiceMetadata.Version},deployment.environment=local,custom.one=kept,custom.two=also-kept,service.namespace=ucdavis",
                configuration["OTEL_RESOURCE_ATTRIBUTES"]);
        }
        finally
        {
            File.Delete(envFile);
        }
    }

    [Fact]
    public void TreatsAMissingEnvFileAsOptional()
    {
        var configuration = new ConfigurationManager();
        var missingFile = Path.Combine(Path.GetTempPath(), $"koi-{Guid.NewGuid():N}.env");

        LocalDevelopmentConfiguration.Add(configuration, missingFile);

        Assert.Null(configuration["ApiKeys:Credentials:0:Id"]);
    }
}
