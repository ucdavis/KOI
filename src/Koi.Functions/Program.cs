using System.Text.Json;
using Koi.Functions.Authentication;
using Koi.Functions.Configuration;
using Koi.Functions.Financial.Configuration;
using Koi.Functions.Financial.Services;
using Koi.Functions.Telemetry;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

var builder = FunctionsApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment()
    || string.Equals(
        Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT"),
        Environments.Development,
        StringComparison.OrdinalIgnoreCase))
{
    LocalDevelopmentConfiguration.Add(builder.Configuration);
}

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

builder.Services
    .AddOptions<FinancialOptions>()
    .BindConfiguration(FinancialOptions.SectionName)
    .Validate(FinancialOptions.IsValid, "Complete, valid Financial API settings must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<ApiKeyAuthenticator>();
builder.Services.AddSingleton<HttpFunctionAuthorizationPolicy>();
builder.Services.AddSingleton<FunctionInvocationMetrics>();
builder.Services.AddScoped<IAggieEnterpriseService, AggieEnterpriseService>();

builder.Build().Run();
