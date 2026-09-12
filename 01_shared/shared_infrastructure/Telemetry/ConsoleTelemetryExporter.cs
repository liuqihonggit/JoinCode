
namespace Core.Telemetry;

/// <summary>
/// 控制台遥测导出器
/// 监听 Activity 完成事件，将 span 信息输出到日志
/// </summary>
public sealed class ConsoleTelemetryExporter : IDisposable
{
    private readonly ILogger? _logger;
    private readonly ActivityListener _listener;
    private int _isDisposed;

    public ConsoleTelemetryExporter(string serviceName, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        _logger = logger;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == serviceName,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };

        ActivitySource.AddActivityListener(_listener);
    }

    private void OnActivityStopped(Activity activity)
    {
        if (DisposableHelper.IsDisposed(ref _isDisposed)) return;

        var status = activity.Status switch
        {
            ActivityStatusCode.Ok => "OK",
            ActivityStatusCode.Error => "ERR",
            _ => "---"
        };

        var duration = activity.Duration.TotalMilliseconds;

        _logger?.LogInformation(
            "[TELEMETRY] {Status} {Name} {Duration:F1}ms TraceId={TraceId} SpanId={SpanId}",
            status,
            activity.DisplayName,
            duration,
            activity.TraceId,
            activity.SpanId);

        // 输出标签
        if (activity.Tags != null)
        {
            foreach (var tag in activity.Tags)
            {
                if (tag.Value != null)
                {
                    _logger?.LogInformation(
                        "[TELEMETRY]   {Key}={Value}",
                        tag.Key,
                        tag.Value);
                }
            }
        }

        // 输出事件
        foreach (var evt in activity.Events)
        {
            _logger?.LogDebug(
                "[TELEMETRY]   Event: {Name}",
                evt.Name);
        }

        // 错误状态额外输出
        if (activity.Status == ActivityStatusCode.Error)
        {
            _logger?.LogWarning(
                "[TELEMETRY] ERROR in {Name}: {Description}",
                activity.DisplayName,
                activity.StatusDescription ?? "no description");
        }
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed)) return;
        _listener.Dispose();
    }
}
