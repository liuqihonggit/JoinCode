
namespace Core.Telemetry;

internal sealed class NoOpTelemetrySpan : ITelemetrySpan, IDisposable
{
    public string SpanId => string.Empty;
    public string TraceId => string.Empty;
    public string? ParentSpanId => null;
    public string Name { get; }
    public TelemetrySpanKind Kind { get; }
    public TelemetryStatusCode Status => TelemetryStatusCode.Unset;
    public bool IsRecording => false;

    internal NoOpTelemetrySpan(string name, TelemetrySpanKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null) => this;
    public ITelemetrySpan SetTag(string key, string value) => this;
    public ITelemetrySpan SetTag(string key, double value) => this;
    public ITelemetrySpan SetTag(string key, bool value) => this;
    public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null) => this;
    public ITelemetrySpan RecordException(Exception exception) => this;
    public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal) => new NoOpTelemetrySpan(name, kind);

    public TelemetrySpanData ToSpanData() => new()
    {
        Name = Name,
        Kind = Kind,
        Status = TelemetryStatusCode.Unset
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

internal sealed class NoOpTelemetryCounter : ITelemetryCounter
{
    public string Name { get; }

    internal NoOpTelemetryCounter(string name) => Name = name;
    public void Add(double value, Dictionary<string, string>? tags = null) { }
}

internal sealed class NoOpTelemetryHistogram : ITelemetryHistogram
{
    public string Name { get; }

    internal NoOpTelemetryHistogram(string name) => Name = name;
    public void Record(double value, Dictionary<string, string>? tags = null) { }
}

internal sealed class NoOpTelemetryGauge : ITelemetryGauge
{
    public string Name { get; }

    internal NoOpTelemetryGauge(string name) => Name = name;
    public void Record(double value, Dictionary<string, string>? tags = null) { }
}
