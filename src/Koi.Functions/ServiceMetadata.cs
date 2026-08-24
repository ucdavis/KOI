namespace Koi.Functions;

internal static class ServiceMetadata
{
    public const string Name = "KOI";

    public static string Version { get; } =
        typeof(ServiceMetadata).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
