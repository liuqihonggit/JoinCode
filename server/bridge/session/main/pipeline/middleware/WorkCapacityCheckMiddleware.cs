namespace Core.Bridge;


/// <summary>
/// 工作容量检查中间件 — 检查活跃会话数是否达上限，达上限则短路跳过；重复工作 ID 同样短路
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkCapacityCheckMiddleware : ServiceEntity, IHandleWorkMiddleware
{
    /// <summary>
    /// 构造工作容量检查中间件
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public WorkCapacityCheckMiddleware(ILogger<WorkCapacityCheckMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<WorkCapacityCheckMiddleware>? _logger;


    /// <summary>
    /// 执行中间件 — 容量检查与重复工作检查，通过则调用下一中间件
    /// </summary>
    /// <param name="ctx">处理工作上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步执行操作的任务</returns>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        _logger?.LogInformation("BridgeMain: received work, WorkId={WorkId}, SessionId={SessionId}, WorkType={WorkType}",
            ctx.Work.WorkId, ctx.Work.SessionId, ctx.Work.WorkType);

        if (ctx.Tracker.Sessions.Count >= ctx.Config.MaxSessions)
        {
            _logger?.LogWarning("BridgeMain: at capacity, skipping work {WorkId}", ctx.Work.WorkId);
            ctx.ShortCircuited = true;
            return;
        }

        if (ctx.Tracker.WorkCompletion.IsCompleted(ctx.Work.WorkId))
        {
            _logger?.LogDebug("BridgeMain: skipping duplicate work {WorkId}", ctx.Work.WorkId);

            if (ctx.Tracker.Sessions.Count >= ctx.Config.MaxSessions)
            {
                var pollConfig = ctx.PollConfig;
                var delayMs = pollConfig?.NonExclusiveHeartbeatIntervalMs > 0
                    ? pollConfig.NonExclusiveHeartbeatIntervalMs
                    : pollConfig?.HeartbeatIntervalMs ?? 30000;
                try
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }

            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
