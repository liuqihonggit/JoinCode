
namespace Core.Tests.Telemetry;

public sealed class TelemetryMetricsTests
{
    private readonly TelemetryConfig _config = new();

    [Fact]
    public void Counter_Add_DoesNotThrow()
    {
        using var service = new TelemetryService(_config);
        var counter = service.GetCounter("test-counter");

        counter.Add(1);
        counter.Add(5, new Dictionary<string, string> { ["method"] = "GET" });
    }

    [Fact]
    public void Counter_AddNegativeValue_DoesNotThrow()
    {
        using var service = new TelemetryService(_config);
        var counter = service.GetCounter("test-counter");

        counter.Add(-1);
    }

    [Fact]
    public void Histogram_Record_DoesNotThrow()
    {
        using var service = new TelemetryService(_config);
        var histogram = service.GetHistogram("test-duration");

        histogram.Record(42.5);
        histogram.Record(100.0, new Dictionary<string, string> { ["endpoint"] = "/api/test" });
    }

    [Fact]
    public void Histogram_RecordZero_DoesNotThrow()
    {
        using var service = new TelemetryService(_config);
        var histogram = service.GetHistogram("test-duration");

        histogram.Record(0);
    }

    [Fact]
    public void Gauge_Record_DoesNotThrow()
    {
        using var service = new TelemetryService(_config);
        var gauge = service.GetGauge("test-gauge");

        gauge.Record(10.0);
        gauge.Record(5.5, new Dictionary<string, string> { ["pool"] = "default" });
    }

    [Fact]
    public void MetricsDisabled_GetCounter_StillReturnsInstance()
    {
        var noMetrics = new TelemetryConfig { MetricsEnabled = false };
        using var service = new TelemetryService(noMetrics);
        var counter = service.GetCounter("test");

        Assert.NotNull(counter);
        counter.Add(1);
    }

    [Fact]
    public void MetricsDisabled_GetHistogram_StillReturnsInstance()
    {
        var noMetrics = new TelemetryConfig { MetricsEnabled = false };
        using var service = new TelemetryService(noMetrics);
        var histogram = service.GetHistogram("test");

        Assert.NotNull(histogram);
        histogram.Record(1.0);
    }
}
