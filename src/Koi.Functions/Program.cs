using System.Text.Json;
using Koi.Functions.Authentication;
using Koi.Functions.Telemetry;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = FunctionsApplication.CreateBuilder(args);

builder.UseMiddleware<InvocationMetricsMiddleware>();
builder.UseMiddleware<ApiKeyAuthenticationMiddleware>();

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(FunctionInvocationMetrics.MeterName))
    .UseFunctionsWorkerDefaults()
    .UseOtlpExporter();

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
builder.Services.AddSingleton<FunctionInvocationMetrics>();

builder.Build().Run();
