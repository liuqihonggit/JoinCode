namespace Infrastructure.Pipeline;

/// <summary>
/// 日志 Scope 工具 — 管道外手动开 scope 的场景
/// </summary>
public static class LogScope
{
    /// <summary>
    /// 开启日志 Scope — using 块内所有日志自动携带 TraceId + ObjectId
    /// </summary>
    public static IDisposable? Begin(ILogger? logger, ObjectId objectId)
    {
        var activity = Activity.Current;
        var state = new LogScopeState(
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            objectId);
        return logger?.BeginScope(state);
    }

    /// <summary>
    /// 仅 TraceId 的轻量 scope（ObjectId = Empty）
    /// </summary>
    public static IDisposable? BeginTrace(ILogger? logger)
    {
        var activity = Activity.Current;
        if (activity is null) return null;
        var state = new LogScopeState(
            activity.TraceId.ToString(),
            activity.SpanId.ToString(),
            ObjectId.Empty);
        return logger?.BeginScope(state);
    }
}
