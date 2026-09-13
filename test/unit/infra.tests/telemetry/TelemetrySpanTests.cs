
namespace Core.Tests.Telemetry;

public sealed class TelemetrySpanTests
{
    private readonly TelemetryConfig _config = new();

    [Fact]
    public async Task SetStatus_UpdatesStatus()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        span.SetStatus(TelemetryStatusCode.Ok, "Success");

        Assert.Equal(TelemetryStatusCode.Ok, span.Status);
    }

    [Fact]
    public async Task SetStatus_Error_SetsStatus()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        span.SetStatus(TelemetryStatusCode.Error, "Something failed");

        Assert.Equal(TelemetryStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task SetTag_String_AddsTag()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        var result = span.SetTag("key", "value");

        Assert.Same(span, result);
    }

    [Fact]
    public async Task SetTag_Double_AddsTag()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        span.SetTag("duration", 42.5);
    }

    [Fact]
    public async Task SetTag_Bool_AddsTag()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        span.SetTag("success", true);
    }

    [Fact]
    public async Task AddEvent_RecordsEvent()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        var result = span.AddEvent("cache-miss", new Dictionary<string, string> { ["key"] = "abc" });

        Assert.Same(span, result);
    }

    [Fact]
    public async Task RecordException_RecordsException()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        var ex = new InvalidOperationException("test error");
        var result = span.RecordException(ex);

        Assert.Same(span, result);
    }

    [Fact]
    public async Task StartChildSpan_CreatesChildSpan()
    {
        await using var service = new TelemetryService(_config);
        await using var parent = service.StartSpan("parent");
        await using var child = parent.StartChildSpan("child", TelemetrySpanKind.Client);

        Assert.Equal("child", child.Name);
        Assert.Equal(parent.SpanId, child.ParentSpanId);
        Assert.Equal(parent.TraceId, child.TraceId);
        Assert.Equal(TelemetrySpanKind.Client, child.Kind);
    }

    [Fact]
    public async Task ToSpanData_ReturnsCompleteData()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test", TelemetrySpanKind.Server);
        span.SetTag("key1", "val1");
        span.SetStatus(TelemetryStatusCode.Ok, "done");

        var data = span.ToSpanData();

        Assert.Equal("test", data.Name);
        Assert.Equal(TelemetrySpanKind.Server, data.Kind);
        Assert.Equal(TelemetryStatusCode.Ok, data.Status);
        Assert.Equal("done", data.StatusDescription);
        Assert.NotNull(data.Tags);
    }

    [Fact]
    public async Task DisposeAsync_StopsRecording()
    {
        await using var service = new TelemetryService(_config);
        var span = service.StartSpan("test");
        Assert.True(span.IsRecording);

        await span.DisposeAsync().ConfigureAwait(true);

        Assert.False(span.IsRecording);
    }

    [Fact]
    public async Task Span_HasValidIds()
    {
        await using var service = new TelemetryService(_config);
        await using var span = service.StartSpan("test");

        Assert.NotEmpty(span.SpanId);
        Assert.NotEmpty(span.TraceId);
    }
}
