
namespace Core.Tests.Telemetry;

public sealed class TelemetryServiceTests
{
    private readonly TelemetryConfig _config = new();

    [Fact]
    public async Task Constructor_SetsConfig()
    {
        await using var service = new TelemetryService(_config);

        Assert.Same(_config, service.Config);
    }

    [Fact]
    public void Constructor_WithNullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TelemetryService(null!));
    }

    [Fact]
    public async Task IsTracingEnabled_ReflectsConfig()
    {
        var tracingOff = new TelemetryConfig { TracingEnabled = false };
        await using var service = new TelemetryService(tracingOff);

        Assert.False(service.IsTracingEnabled);
    }

    [Fact]
    public async Task IsMetricsEnabled_ReflectsConfig()
    {
        var metricsOff = new TelemetryConfig { MetricsEnabled = false };
        await using var service = new TelemetryService(metricsOff);

        Assert.False(service.IsMetricsEnabled);
    }

    [Fact]
    public async Task StartSpan_ReturnsSpan()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test-operation");

        Assert.NotNull(span);
        Assert.Equal("test-operation", span.Name);
        Assert.False(string.IsNullOrEmpty(span.SpanId));
        Assert.False(string.IsNullOrEmpty(span.TraceId));
        Assert.True(span.IsRecording);
    }

    [Fact]
    public async Task StartSpan_WithKind_SetsKind()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("client-call", TelemetrySpanKind.Client);

        Assert.Equal(TelemetrySpanKind.Client, span.Kind);
    }

    [Fact]
    public async Task StartSpan_WithParent_SetsParentSpanId()
    {
        await using var service = new TelemetryService(_config);
        await using var parent = service.StartSpan("parent");
        await using var child = service.StartSpan("child", TelemetrySpanKind.Internal, parent);

        Assert.Equal(parent.SpanId, child.ParentSpanId);
        Assert.Equal(parent.TraceId, child.TraceId);
    }

    [Fact]
    public async Task StartSpan_TracingDisabled_ReturnsNoOpSpan()
    {
        var noTracing = new TelemetryConfig { TracingEnabled = false };
        await using var service = new TelemetryService(noTracing);
        await using var span = service.StartSpan("no-op");

        Assert.NotNull(span);
        Assert.False(span.IsRecording);
    }

    [Fact]
    public async Task GetCounter_ReturnsCounter()
    {
        await using var service = new TelemetryService(_config);
        var counter = service.GetCounter("request-count", "requests", "Total requests");

        Assert.NotNull(counter);
        Assert.Equal("request-count", counter.Name);
    }

    [Fact]
    public async Task GetCounter_SameName_ReturnsSameInstance()
    {
        await using var service = new TelemetryService(_config);
        var counter1 = service.GetCounter("request-count");
        var counter2 = service.GetCounter("request-count");

        Assert.Same(counter1, counter2);
    }

    [Fact]
    public async Task GetHistogram_ReturnsHistogram()
    {
        await using var service = new TelemetryService(_config);
        var histogram = service.GetHistogram("request-duration", "ms", "Request duration");

        Assert.NotNull(histogram);
        Assert.Equal("request-duration", histogram.Name);
    }

    [Fact]
    public async Task GetHistogram_SameName_ReturnsSameInstance()
    {
        await using var service = new TelemetryService(_config);
        var hist1 = service.GetHistogram("duration");
        var hist2 = service.GetHistogram("duration");

        Assert.Same(hist1, hist2);
    }

    [Fact]
    public async Task GetGauge_ReturnsGauge()
    {
        await using var service = new TelemetryService(_config);
        var gauge = service.GetGauge("active-sessions", "sessions", "Active sessions");

        Assert.NotNull(gauge);
        Assert.Equal("active-sessions", gauge.Name);
    }

    [Fact]
    public async Task GetActiveSpans_ReturnsActiveSpans()
    {
        await using var service = new TelemetryService(_config);
        var span1 = service.StartSpan("op1");
        var span2 = service.StartSpan("op2");

        var active = service.GetActiveSpans();
        Assert.True(active.Count >= 2);

        await span1.DisposeAsync();
        await span2.DisposeAsync();
    }

    [Fact]
    public async Task GetRegisteredMetrics_ReturnsMetricNames()
    {
        await using var service = new TelemetryService(_config);
        service.GetCounter("c1");
        service.GetHistogram("h1");

        var metrics = service.GetRegisteredMetrics();
        Assert.True(metrics.Count >= 2);
    }

    [Fact]
    public async Task DisposeAsync_CleansUp()
    {
        var service = new TelemetryService(_config);
        await service.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var service = new TelemetryService(_config);
        await service.DisposeAsync().ConfigureAwait(true);
        await service.DisposeAsync().ConfigureAwait(true);
    }
}
