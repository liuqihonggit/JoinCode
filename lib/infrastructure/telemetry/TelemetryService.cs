using System.Threading;

namespace Core.Telemetry;

/// <summary>
/// 遥测服务 — 提供 Span 追踪与 Counter/Histogram/Gauge 指标采集能力
/// <para>基于 System.Diagnostics.ActivitySource 与 Meter 实现,支持控制台导出器插件化加载</para>
/// </summary>
[Register(typeof(ITelemetryService), ServiceLifetime.Singleton)]
public sealed partial class TelemetryService : ITelemetryService
{
    private readonly TelemetryConfig _config;
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly ActivityListener _listener;
    private ConsoleTelemetryExporter? _consoleExporter;
    private readonly IAnalyticsFileSink? _analyticsSink;
    private readonly ConcurrentDictionary<string, ITelemetryMetric> _metrics = new();
    private readonly ConcurrentDictionary<string, TelemetrySpan> _activeSpans = new();
    private int _isDisposed;

    /// <summary>遥测配置快照</summary>
    public TelemetryConfig Config => _config;
    /// <summary>是否启用链路追踪</summary>
    public bool IsTracingEnabled => _config.TracingEnabled;
    /// <summary>是否启用指标采集</summary>
    public bool IsMetricsEnabled => _config.MetricsEnabled;

    /// <summary>
    /// 构造遥测服务
    /// </summary>
    /// <param name="config">遥测配置,定义服务名、采样策略与默认标签</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="analyticsSink">可选分析文件下沉器,用于将指标写入文件</param>
    public TelemetryService(TelemetryConfig config, ILogger? logger = null, IAnalyticsFileSink? analyticsSink = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _analyticsSink = analyticsSink;
        _activitySource = new ActivitySource(config.ServiceName, config.ServiceVersion);
        _meter = new Meter(config.ServiceName, config.ServiceVersion);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == config.ServiceName,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    /// 启用控制台导出器 — 插件加载时调用(ADR 0098 万物皆插件)
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public void EnableConsoleExporter(ILogger? logger)
    {
        _consoleExporter?.Dispose();
        _consoleExporter = new ConsoleTelemetryExporter(_config.ServiceName, logger);
    }

    /// <summary>
    /// 禁用控制台导出器 — 插件卸载时调用
    /// </summary>
    public void DisableConsoleExporter()
    {
        _consoleExporter?.Dispose();
        _consoleExporter = null;
    }

    /// <inheritdoc/>
    public ITelemetrySpan StartSpan(
        string name,
        TelemetrySpanKind kind = TelemetrySpanKind.Internal,
        ITelemetrySpan? parent = null)
    {
        if (!_config.TracingEnabled)
        {
            return new NoOpTelemetrySpan(name, kind);
        }

        var activityKind = MapActivityKind(kind);
        var parentActivity = parent is TelemetrySpan realSpan
            ? realSpan.UnderlyingActivity
            : null;

        var activity = parentActivity != null
            ? _activitySource.StartActivity(name, activityKind, parentActivity.Context)
            : _activitySource.StartActivity(name, activityKind);

        if (activity == null)
        {
            return new NoOpTelemetrySpan(name, kind);
        }

        foreach (var (key, value) in _config.DefaultTags)
        {
            activity.SetTag(key, value);
        }

        var span = new TelemetrySpan(activity, kind, this);
        _activeSpans[activity.SpanId.ToString()] = span;
        return span;
    }

    /// <inheritdoc/>
    public ITelemetryCounter GetCounter(string name, string? unit = null, string? description = null)
    {
        var metric = _metrics.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryCounter(n);
            }

            var counter = _meter.CreateCounter<double>(n, unit, description);
            return new TelemetryCounter(n, counter, _analyticsSink);
        });
        return metric is ITelemetryCounter counter
            ? counter
            : throw new InvalidOperationException(
                $"指标 '{name}' 已注册为 {metric.GetType().Name},不能作为 Counter 获取。请使用与注册时一致的指标类型访问。");
    }

    /// <inheritdoc/>
    public ITelemetryHistogram GetHistogram(string name, string? unit = null, string? description = null)
    {
        var metric = _metrics.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryHistogram(n);
            }

            var histogram = _meter.CreateHistogram<double>(n, unit, description);
            return new TelemetryHistogram(n, histogram);
        });
        return metric is ITelemetryHistogram histogram
            ? histogram
            : throw new InvalidOperationException(
                $"指标 '{name}' 已注册为 {metric.GetType().Name},不能作为 Histogram 获取。请使用与注册时一致的指标类型访问。");
    }

    /// <inheritdoc/>
    public ITelemetryGauge GetGauge(string name, string? unit = null, string? description = null)
    {
        var metric = _metrics.GetOrAdd(name, n =>
        {
            if (!_config.MetricsEnabled)
            {
                return new NoOpTelemetryGauge(n);
            }

            var gauge = _meter.CreateGauge<double>(n, unit, description);
            return new TelemetryGauge(n, gauge);
        });
        return metric is ITelemetryGauge gauge
            ? gauge
            : throw new InvalidOperationException(
                $"指标 '{name}' 已注册为 {metric.GetType().Name},不能作为 Gauge 获取。请使用与注册时一致的指标类型访问。");
    }

    /// <inheritdoc/>
    public IEnumerable<TelemetrySpanData> GetActiveSpans()
    {
        return _activeSpans.Values
            .Select(s => s.ToSpanData());
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetRegisteredMetrics()
    {
        return _metrics.Keys;
    }

    /// <summary>
    /// 从活动 Span 表中移除指定 Span — 由 TelemetrySpan.DisposeAsync 调用
    /// </summary>
    /// <param name="spanId">要移除的 Span 标识</param>
    internal void RemoveActiveSpan(string spanId)
    {
        _activeSpans.TryRemove(spanId, out _);
    }

    /// <summary>
    /// 异步释放遥测服务 — 关闭所有活动 Span、导出器、监听器、ActivitySource 与 Meter
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _consoleExporter?.Dispose();
        _listener.Dispose();
        _activitySource.Dispose();
        _meter.Dispose();
        return new ValueTask(Task.WhenAll(_activeSpans.Values.ToList().Select(span => span.DisposeAsync().AsTask())));
    }

    private static ActivityKind MapActivityKind(TelemetrySpanKind kind) => kind switch
    {
        TelemetrySpanKind.Internal => ActivityKind.Internal,
        TelemetrySpanKind.Server => ActivityKind.Server,
        TelemetrySpanKind.Client => ActivityKind.Client,
        TelemetrySpanKind.Producer => ActivityKind.Producer,
        TelemetrySpanKind.Consumer => ActivityKind.Consumer,
        _ => ActivityKind.Internal
    };
}
