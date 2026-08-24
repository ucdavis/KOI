using System.Net;
using Koi.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Koi.Functions.Authentication;

public sealed partial class ApiKeyAuthenticationMiddleware(
    ApiKeyAuthenticator authenticator,
    HttpFunctionAuthorizationPolicy policy,
    ILogger<ApiKeyAuthenticationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!IsHttpTrigger(context) || policy.IsAnonymous(context.FunctionDefinition.Name))
        {
            await next(context);
            return;
        }

        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            throw new InvalidOperationException(
                $"HTTP request data is unavailable for function '{context.FunctionDefinition.Name}'.");
        }

        request.Headers.TryGetValues("Authorization", out var authorizationHeaders);
        var authentication = authenticator.Authenticate(authorizationHeaders);

        if (!authentication.IsAuthenticated)
        {
            var response = request.CreateResponse();
            response.StatusCode = HttpStatusCode.Unauthorized;
            response.Headers.Add("WWW-Authenticate", "Bearer");
            await response.WriteAsJsonAsync(
                new ErrorResponse("unauthorized"),
                context.CancellationToken);
            context.GetInvocationResult().Value = response;
            return;
        }

        LogAuthenticated(
            logger,
            context.FunctionDefinition.Name,
            authentication.KeyId);

        await next(context);
    }

    private static bool IsHttpTrigger(FunctionContext context)
    {
        return context.FunctionDefinition.InputBindings.Values.Any(binding =>
            string.Equals(binding.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Authenticated {FunctionName} with API key {ApiKeyId}")]
    private static partial void LogAuthenticated(
        ILogger logger,
        string functionName,
        string? apiKeyId);
}
