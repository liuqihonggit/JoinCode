namespace Core.Context.Compact;

/// <summary>
/// 压缩遥测中间件 — 在管道执行完毕后记录压缩操作计数
/// </summary>
[Register(typeof(ICompactMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CompactTelemetryMiddleware : ServiceEntity, ICompactMiddleware
{
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 初始化 <see cref="CompactTelemetryMiddleware"/> 实例
    /// </summary>
    /// <param name="telemetryService">可选遥测服务，为 null 时跳过记录</param>
    public CompactTelemetryMiddleware(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    /// <summary>中间件异常时的行为：继续传递给下一个中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — 先传递给下一中间件，完成后记录压缩操作遥测
    /// </summary>
    /// <param name="context">压缩上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(CompactContext context, MiddlewareDelegate<CompactContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (_telemetryService is not null && context.Result is not null)
        {
            _telemetryService.RecordCount("compact.operation.count",
                new() { ["trigger"] = context.Request.Trigger.ToString(), ["level"] = context.Result.Level.ToString() },
                "count", "Compact operation count");
        }
    }
}
