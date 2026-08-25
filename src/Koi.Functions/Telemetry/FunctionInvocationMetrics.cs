using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Koi.Functions.Telemetry;

internal sealed class FunctionInvocationMetrics : IDisposable
{
    internal const string MeterName = "Koi.Functions";

    private readonly Meter _meter = new(MeterName, ServiceMetadata.Version);
    private readonly Counter<long> _invocations;
    private readonly Histogram<double> _duration;

    public FunctionInvocationMetrics()
    {
        _invocations = _meter.CreateCounter<long>(
            "koi.function.invocations",
            unit: "{invocation}");
        _duration = _meter.CreateHistogram<double>(
            "koi.function.duration",
            unit: "s");
    }

    public void Record(string functionName, TimeSpan duration, bool failed)
    {
        TagList tags = new()
        {
            { "function.name", functionName },
            { "function.outcome", failed ? "failure" : "success" },
        };

        _invocations.Add(1, tags);
        _duration.Record(duration.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}
