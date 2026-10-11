namespace Core.Agents;

/// <summary>
/// 记录上下文中间件 — 记录 SpawnedAt 时间戳 + 创建 AgentExecutionContext
/// 合并自路径 B 的 SpawnCoordRecordContextMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RecordContextMiddleware : ServiceEntity, IUnifiedSpawnMiddleware {

    /// <summary>
    /// 构造 RecordContextMiddleware 实例，注入时钟服务
    /// </summary>
    public RecordContextMiddleware(IClockService clock) {
        _clock = clock;
    }
    private readonly IClockService _clock;

    /// <summary>中间件错误处理策略：向上传播</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 执行上下文记录：代理已创建时记录 SpawnedAt 时间戳并创建执行上下文
    /// </summary>
    /// <param name="context">统一 Spawn 上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct) {
        if (context.Agent is not null) {
            var now = _clock.GetUtcNow();
            context.SpawnedAt = now;
            context.ExecutionContext = new AgentExecutionContext {
                AgentId = context.AgentId,
                Task = context.Task,
                SpawnedAt = now,
                RetryCount = 0
            };
        }

        return next(context, ct);
    }
}