using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Koi.Functions.Telemetry;

internal sealed class InvocationMetricsMiddleware(FunctionInvocationMetrics metrics)
    : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var failed = false;

        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            metrics.Record(
                context.FunctionDefinition.Name,
                Stopwatch.GetElapsedTime(startedAt),
                failed);
        }
    }
}
