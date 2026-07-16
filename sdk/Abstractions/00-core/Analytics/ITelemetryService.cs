
namespace JoinCode.Abstractions.Interfaces;

public interface ITelemetryService : IAsyncDisposable
{
    TelemetryConfig Config { get; }

    bool IsTracingEnabled { get; }

    bool IsMetricsEnabled { get; }

    ITelemetrySpan StartSpan(
        string name,
        TelemetrySpanKind kind = TelemetrySpanKind.Internal,
        ITelemetrySpan? parent = null);

    ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null);

    ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null);

    ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null);

    IReadOnlyList<TelemetrySpanData> GetActiveSpans();

    IReadOnlyList<string> GetRegisteredMetrics();
}

public interface ITelemetrySpan : IAsyncDisposable
{
    string SpanId { get; }

    string TraceId { get; }

    string? ParentSpanId { get; }

    string Name { get; }

    TelemetrySpanKind Kind { get; }

    TelemetryStatusCode Status { get; }

    bool IsRecording { get; }

    ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null);

    ITelemetrySpan SetTag(string key, string value);

    ITelemetrySpan SetTag(string key, double value);

    ITelemetrySpan SetTag(string key, bool value);

    ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null);

    ITelemetrySpan RecordException(Exception exception);

    ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal);

    TelemetrySpanData ToSpanData();
}

public interface ITelemetryCounter
{
    string Name { get; }

    void Add(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryHistogram
{
    string Name { get; }

    void Record(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryGauge
{
    string Name { get; }

    void Record(double value, Dictionary<string, string>? tags = null);
}
