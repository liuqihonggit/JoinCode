
namespace Core.Telemetry;

/// <summary>
/// 遥测 Span 实现 — 封装 System.Diagnostics.Activity,提供链路追踪能力
/// </summary>
public sealed class TelemetrySpan : ITelemetrySpan
{
    private readonly Activity _activity;
    private readonly TelemetrySpanKind _kind;
    private readonly TelemetryService _service;
    private readonly List<TelemetrySpanEvent> _events = [];
    private int _isDisposed;

    /// <summary>底层 Activity 实例</summary>
    public Activity UnderlyingActivity => _activity;

    /// <summary>Span 标识</summary>
    public string SpanId => _activity.SpanId.ToString();
    /// <summary>Trace 标识</summary>
    public string TraceId => _activity.TraceId.ToString();
    /// <summary>父 Span 标识</summary>
    public string? ParentSpanId => _activity.ParentSpanId.ToString();
    /// <summary>Span 名称</summary>
    public string Name => _activity.DisplayName;
    /// <summary>Span 类型</summary>
    public TelemetrySpanKind Kind => _kind;
    /// <summary>状态码</summary>
    public TelemetryStatusCode Status { get; private set; } = TelemetryStatusCode.Unset;
    /// <summary>状态描述</summary>
    public string? StatusDescription { get; private set; }
    /// <summary>是否仍在记录</summary>
    public bool IsRecording => !DisposableHelper.IsDisposed(ref _isDisposed) && _activity.IsAllDataRequested;

    /// <summary>
    /// 内部构造 — 由 TelemetryService.StartSpan 工厂调用
    /// </summary>
    /// <param name="activity">底层 Activity 实例</param>
    /// <param name="kind">Span 类型</param>
    /// <param name="service">所属遥测服务,用于释放时从活动 Span 表中移除</param>
    internal TelemetrySpan(Activity activity, TelemetrySpanKind kind, TelemetryService service)
    {
        _activity = activity;
        _kind = kind;
        _service = service;
    }

    /// <inheritdoc/>
    public ITelemetrySpan SetStatus(TelemetryStatusCode statusCode, string? description = null)
    {
        Status = statusCode;
        StatusDescription = description;
        _activity.SetStatus(MapActivityStatus(statusCode), description);
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan SetTag(string key, string value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan SetTag(string key, double value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan SetTag(string key, bool value)
    {
        _activity.SetTag(key, value);
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan AddEvent(string name, Dictionary<string, string>? tags = null)
    {
        var evt = new TelemetrySpanEvent
        {
            Name = name,
            Timestamp = DateTimeOffset.UtcNow,
            Tags = tags ?? []
        };
        _events.Add(evt);

        var activityEvent = new ActivityEvent(name);
        _activity.AddEvent(activityEvent);
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan RecordException(Exception exception)
    {
        _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity.AddEvent(new ActivityEvent("exception",
            tags: new ActivityTagsCollection
            {
                ["exception.type"] = exception.GetType().FullName,
                ["exception.message"] = exception.Message,
                ["exception.stacktrace"] = exception.StackTrace ?? string.Empty
            }));
        Status = TelemetryStatusCode.Error;
        return this;
    }

    /// <inheritdoc/>
    public ITelemetrySpan StartChildSpan(string name, TelemetrySpanKind kind = TelemetrySpanKind.Internal)
    {
        return _service.StartSpan(name, kind, this);
    }

    /// <summary>
    /// 转换为 Span 快照数据
    /// </summary>
    /// <returns>包含 Span 元信息、时间戳、标签与事件的快照对象</returns>
    public TelemetrySpanData ToSpanData()
    {
        return new TelemetrySpanData
        {
            Name = _activity.DisplayName,
            SpanId = _activity.SpanId.ToString(),
            TraceId = _activity.TraceId.ToString(),
            ParentSpanId = _activity.ParentSpanId.ToString(),
            Kind = _kind,
            Status = Status,
            StatusDescription = StatusDescription,
            StartTime = _activity.StartTimeUtc,
            EndTime = _activity.StartTimeUtc + _activity.Duration,
            Duration = _activity.Duration,
            Tags = _activity.Tags?.ToDictionary(t => t.Key, t => t.Value?.ToString() ?? string.Empty) ?? [],
            Events = _events.ToList()
        };
    }

    /// <summary>
    /// 异步释放 Span — 关闭底层 Activity 并从活动 Span 表中移除
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return ValueTask.CompletedTask;
        }

        _activity.Dispose();
        _service.RemoveActiveSpan(SpanId);
        return ValueTask.CompletedTask;
    }

    private static ActivityStatusCode MapActivityStatus(TelemetryStatusCode statusCode) => statusCode switch
    {
        TelemetryStatusCode.Ok => ActivityStatusCode.Ok,
        TelemetryStatusCode.Error => ActivityStatusCode.Error,
        _ => ActivityStatusCode.Unset
    };
}
