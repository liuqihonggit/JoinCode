
namespace JoinCode.Abstractions.Interfaces;

public interface ITelemetryService : IAsyncDisposable {
    /// <summary>获取遥测配置。</summary>
    TelemetryConfig Config { get; }

    /// <summary>获取是否启用链路追踪。</summary>
    bool IsTracingEnabled { get; }

    /// <summary>获取是否启用指标采集。</summary>
    bool IsMetricsEnabled { get; }

    /// <summary>开始一个新的追踪跨度。</summary>
    ITelemetrySpan StartSpan(
        string name,
        TelemetrySpanKind kind = TelemetrySpanKind.Internal,
        ITelemetrySpan? parent = null);

    /// <summary>获取或创建计数器指标。</summary>
    ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null);

    /// <summary>获取或创建直方图指标。</summary>
    ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null);

    /// <summary>获取或创建仪表指标。</summary>
    ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null);

    /// <summary>获取所有活动跨度。</summary>
    IEnumerable<TelemetrySpanData> GetActiveSpans();

    /// <summary>获取已注册指标名称列表。</summary>
    IEnumerable<string> GetRegisteredMetrics();
}

public interface ITelemetrySpan : IAsyncDisposable {
    /// <summary>获取跨度标识。</summary>
    string SpanId { get; }

    /// <summary>获取追踪标识。</summary>
    string TraceId { get; }

    /// <summary>获取父跨度标识。</summary>
    string? ParentSpanId { get; }

    /// <summary>获取跨度名称。</summary>
    string Name { get; }

    /// <summary>获取跨度类型。</summary>
    TelemetrySpanKind Kind { get; }

    /// <summary>获取跨度状态码。</summary>
    TelemetryStatusCode Status { get; }

    /// <summary>获取是否正在记录。</summary>
    bool IsRecording { get; }

    /// <summary>设置跨度状态。</summary>
    ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null);

    /// <summary>设置字符串标签。</summary>
    ITelemetrySpan SetTag(string key, string value);

    /// <summary>设置数值标签。</summary>
    ITelemetrySpan SetTag(string key, double value);

    /// <summary>设置布尔标签。</summary>
    ITelemetrySpan SetTag(string key, bool value);

    /// <summary>添加事件。</summary>
    ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null);

    /// <summary>记录异常。</summary>
    ITelemetrySpan RecordException(Exception exception);

    /// <summary>开始子跨度。</summary>
    ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal);

    /// <summary>转换为跨度数据快照。</summary>
    TelemetrySpanData ToSpanData();
}

/// <summary>
/// 遥测指标公共契约 — 所有指标类型(Counter/Histogram/Gauge)的公共基接口
/// <para>用于统一存储与遍历已注册指标,避免维护多个并行字典</para>
/// </summary>
public interface ITelemetryMetric {
    /// <summary>获取指标名称</summary>
    string Name { get; }
}

public interface ITelemetryCounter : ITelemetryMetric {
    /// <summary>累加计数器值。</summary>
    void Add(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryHistogram : ITelemetryMetric {
    /// <summary>记录直方图观测值。</summary>
    void Record(double value, Dictionary<string, string>? tags = null);
}

public interface ITelemetryGauge : ITelemetryMetric {
    /// <summary>记录仪表当前值。</summary>
    void Record(double value, Dictionary<string, string>? tags = null);
}