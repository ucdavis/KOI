using System.Text.Json;
using Koi.Functions.Authentication;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();

builder.Services.Configure<JsonSerializerOptions>(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PropertyNameCaseInsensitive = false;
});

builder.Services
    .AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .Validate(ApiKeyOptions.IsValid, "Exactly two unique, valid API key slots with at least one enabled slot must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<ApiKeyAuthenticator>();
builder.Services.AddSingleton<HttpFunctionAuthorizationPolicy>();

builder.Build().Run();
