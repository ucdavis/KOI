using Microsoft.Extensions.Configuration;

namespace Koi.Functions.Configuration;

internal static class LocalDevelopmentConfiguration
{
    private static readonly HashSet<string> RequiredResourceAttributeNames =
    [
        "service.name",
        "service.version",
        "deployment.environment",
        "service.namespace"
    ];

    public static void Add(ConfigurationManager configuration, string envFilePath = ".env")
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration
            .AddEnvFile(envFilePath, optional: true)
            .AddEnvironmentVariables();

        if (string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return;
        }

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_RESOURCE_ATTRIBUTES"] = BuildResourceAttributes(
                configuration["OTEL_RESOURCE_ATTRIBUTES"]),
            ["OTEL_SERVICE_NAME"] = "koi"
        });
    }

    internal static string BuildResourceAttributes(string? configuredAttributes)
    {
        var attributes = new List<string>
        {
            "service.name=koi",
            $"service.version={ServiceMetadata.Version}",
            "deployment.environment=local"
        };

        if (!string.IsNullOrWhiteSpace(configuredAttributes))
        {
            attributes.AddRange(configuredAttributes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(attribute => !IsRequiredResourceAttribute(attribute)));
        }

        attributes.Add("service.namespace=ucdavis");
        return string.Join(',', attributes);
    }

    private static bool IsRequiredResourceAttribute(string attribute)
    {
        var separatorIndex = attribute.IndexOf('=');
        var name = separatorIndex < 0 ? attribute : attribute[..separatorIndex];
        return RequiredResourceAttributeNames.Contains(name.Trim());
    }
}
