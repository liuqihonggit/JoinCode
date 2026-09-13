
namespace Core.Telemetry;

/// <summary>
/// 空操作遥测 Span — 链路追踪禁用时的无操作实现,所有方法均为空操作
/// </summary>
internal sealed class NoOpTelemetrySpan : ITelemetrySpan
{
    /// <summary>获取 Span 标识（空操作实现,返回空字符串）</summary>
    public string SpanId => string.Empty;
    /// <summary>获取 Trace 标识（空操作实现,返回空字符串）</summary>
    public string TraceId => string.Empty;
    /// <summary>获取父 Span 标识（空操作实现,返回 null）</summary>
    public string? ParentSpanId => null;
    /// <summary>获取 Span 名称</summary>
    public string Name { get; }
    /// <summary>获取 Span 类型</summary>
    public TelemetrySpanKind Kind { get; }
    /// <summary>获取状态码（空操作实现,返回 Unset）</summary>
    public TelemetryStatusCode Status => TelemetryStatusCode.Unset;
    /// <summary>获取是否仍在记录（空操作实现,返回 false）</summary>
    public bool IsRecording => false;

    /// <summary>
    /// 构造空操作遥测 Span
    /// </summary>
    /// <param name="name">Span 名称</param>
    /// <param name="kind">Span 类型</param>
    internal NoOpTelemetrySpan(string name, TelemetrySpanKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>设置状态（空操作实现,返回自身）</summary>
    public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null) => this;
    /// <summary>设置字符串标签（空操作实现,返回自身）</summary>
    public ITelemetrySpan SetTag(string key, string value) => this;
    /// <summary>设置数值标签（空操作实现,返回自身）</summary>
    public ITelemetrySpan SetTag(string key, double value) => this;
    /// <summary>设置布尔标签（空操作实现,返回自身）</summary>
    public ITelemetrySpan SetTag(string key, bool value) => this;
    /// <summary>添加事件（空操作实现,返回自身）</summary>
    public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null) => this;
    /// <summary>记录异常（空操作实现,返回自身）</summary>
    public ITelemetrySpan RecordException(Exception exception) => this;
    /// <summary>启动子 Span（空操作实现,返回新的空操作 Span）</summary>
    public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal) => new NoOpTelemetrySpan(name, kind);

    /// <summary>转换为 Span 快照数据（空操作实现,仅填充名称与类型）</summary>
    public TelemetrySpanData ToSpanData() => new()
    {
        Name = Name,
        Kind = Kind,
        Status = TelemetryStatusCode.Unset
    };

    /// <summary>异步释放（空操作实现,返回已完成任务）</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// 空操作遥测计数器 — 指标采集禁用时的无操作实现,Add 不执行任何操作
/// </summary>
internal sealed class NoOpTelemetryCounter : ITelemetryCounter
{
    /// <summary>获取计数器名称</summary>
    public string Name { get; }

    /// <summary>
    /// 构造空操作遥测计数器
    /// </summary>
    /// <param name="name">计数器名称</param>
    internal NoOpTelemetryCounter(string name) => Name = name;
    /// <summary>累加计数（空操作实现,不执行任何操作）</summary>
    public void Add(double value, Dictionary<string, string>? tags = null) { }
}

/// <summary>
/// 空操作遥测直方图 — 指标采集禁用时的无操作实现,Record 不执行任何操作
/// </summary>
internal sealed class NoOpTelemetryHistogram : ITelemetryHistogram
{
    /// <summary>获取直方图名称</summary>
    public string Name { get; }

    /// <summary>
    /// 构造空操作遥测直方图
    /// </summary>
    /// <param name="name">直方图名称</param>
    internal NoOpTelemetryHistogram(string name) => Name = name;
    /// <summary>记录观测值（空操作实现,不执行任何操作）</summary>
    public void Record(double value, Dictionary<string, string>? tags = null) { }
}

/// <summary>
/// 空操作遥测仪表盘 — 指标采集禁用时的无操作实现,Record 不执行任何操作
/// </summary>
internal sealed class NoOpTelemetryGauge : ITelemetryGauge
{
    /// <summary>获取仪表盘名称</summary>
    public string Name { get; }

    /// <summary>
    /// 构造空操作遥测仪表盘
    /// </summary>
    /// <param name="name">仪表盘名称</param>
    internal NoOpTelemetryGauge(string name) => Name = name;
    /// <summary>记录瞬时值（空操作实现,不执行任何操作）</summary>
    public void Record(double value, Dictionary<string, string>? tags = null) { }
}
