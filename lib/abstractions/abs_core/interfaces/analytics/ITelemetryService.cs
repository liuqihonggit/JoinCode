
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

    IEnumerable<TelemetrySpanData> GetActiveSpans();

    IEnumerable<string> GetRegisteredMetrics();
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

/// <summary>
/// 遥测指标公共契约 — 所有指标类型(Counter/Histogram/Gauge)的公共基接口
/// <para>用于统一存储与遍历已注册指标,避免维护多个并行字典</para>
/// </summary>
public interface ITelemetryMetric
{
    /// <summary>获取指标名称</summary>
    string Name { get; }
}

public interface ITelemetryCounter : ITelemetryMetric
{
    void Add(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryHistogram : ITelemetryMetric
{
    void Record(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryGauge : ITelemetryMetric
{
    void Record(double value, Dictionary<string, string>? tags = null);
}
