using System.Diagnostics.Metrics;
using Koi.Functions.Telemetry;

namespace Koi.Functions.Tests.Telemetry;

public sealed class FunctionInvocationMetricsTests
{
    [Fact]
    public void RecordEmitsInvocationCountAndDuration()
    {
        List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> measurements = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == FunctionInvocationMetrics.MeterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();
        using FunctionInvocationMetrics metrics = new();

        metrics.Record("Hello", TimeSpan.FromMilliseconds(250), failed: false);

        Assert.Collection(
            measurements.OrderBy(measurement => measurement.Instrument),
            duration =>
            {
                Assert.Equal("koi.function.duration", duration.Instrument);
                Assert.Equal(0.25, duration.Value);
                Assert.Contains(duration.Tags, tag =>
                    tag.Key == "function.name" && Equals(tag.Value, "Hello"));
            },
            invocations =>
            {
                Assert.Equal("koi.function.invocations", invocations.Instrument);
                Assert.Equal(1, invocations.Value);
                Assert.Contains(invocations.Tags, tag =>
                    tag.Key == "function.outcome" && Equals(tag.Value, "success"));
            });
    }
}
