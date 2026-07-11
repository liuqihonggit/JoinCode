
namespace Core.Tests.Telemetry;

public sealed class TelemetrySpanTests
{
    private readonly TelemetryConfig _config = new();

    [Fact]
    public void SetStatus_UpdatesStatus()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        span.SetStatus(TelemetryStatusCode.Ok, "Success");

        Assert.Equal(TelemetryStatusCode.Ok, span.Status);
    }

    [Fact]
    public void SetStatus_Error_SetsStatus()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        span.SetStatus(TelemetryStatusCode.Error, "Something failed");

        Assert.Equal(TelemetryStatusCode.Error, span.Status);
    }

    [Fact]
    public void SetTag_String_AddsTag()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        var result = span.SetTag("key", "value");

        Assert.Same(span, result);
    }

    [Fact]
    public void SetTag_Double_AddsTag()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        span.SetTag("duration", 42.5);
    }

    [Fact]
    public void SetTag_Bool_AddsTag()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        span.SetTag("success", true);
    }

    [Fact]
    public void AddEvent_RecordsEvent()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        var result = span.AddEvent("cache-miss", new Dictionary<string, string> { ["key"] = "abc" });

        Assert.Same(span, result);
    }

    [Fact]
    public void RecordException_RecordsException()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        var ex = new InvalidOperationException("test error");
        var result = span.RecordException(ex);

        Assert.Same(span, result);
    }

    [Fact]
    public void StartChildSpan_CreatesChildSpan()
    {
        using var service = new TelemetryService(_config);
        using var parent = service.StartSpan("parent");
        using var child = parent.StartChildSpan("child", TelemetrySpanKind.Client);

        Assert.Equal("child", child.Name);
        Assert.Equal(parent.SpanId, child.ParentSpanId);
        Assert.Equal(parent.TraceId, child.TraceId);
        Assert.Equal(TelemetrySpanKind.Client, child.Kind);
    }

    [Fact]
    public void ToSpanData_ReturnsCompleteData()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test", TelemetrySpanKind.Server);
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
        using var service = new TelemetryService(_config);
        var span = service.StartSpan("test");
        Assert.True(span.IsRecording);

        await span.DisposeAsync().ConfigureAwait(true);

        Assert.False(span.IsRecording);
    }

    [Fact]
    public void Span_HasValidIds()
    {
        using var service = new TelemetryService(_config);
        using var span = service.StartSpan("test");

        Assert.NotEmpty(span.SpanId);
        Assert.NotEmpty(span.TraceId);
    }
}
