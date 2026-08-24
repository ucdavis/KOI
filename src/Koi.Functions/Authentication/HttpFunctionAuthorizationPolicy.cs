using System.Reflection;
using Microsoft.Azure.Functions.Worker;

namespace Koi.Functions.Authentication;

public sealed class HttpFunctionAuthorizationPolicy
{
    private readonly HashSet<string> _anonymousFunctions = DiscoverAnonymousFunctions();

    public bool IsAnonymous(string functionName) => _anonymousFunctions.Contains(functionName);

    private static HashSet<string> DiscoverAnonymousFunctions()
    {
        return typeof(HttpFunctionAuthorizationPolicy).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.IsDefined(typeof(AllowAnonymousAttribute), inherit: false))
            .Select(method => method.GetCustomAttribute<FunctionAttribute>(inherit: false)?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal)!;
    }
}
