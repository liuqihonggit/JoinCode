namespace Core.Agents;

/// <summary>
/// 记录上下文中间件 — 记录 SpawnedAt 时间戳 + 创建 AgentExecutionContext
/// 合并自路径 B 的 SpawnCoordRecordContextMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RecordContextMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public RecordContextMiddleware(IClockService clock)
    {
        _clock = clock;
    }
    private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.Agent is not null)
        {
            var now = _clock.GetUtcNow();
            context.SpawnedAt = now;
            context.ExecutionContext = new AgentExecutionContext
            {
                AgentId = context.AgentId,
                Task = context.Task,
                SpawnedAt = now,
                RetryCount = 0
            };
        }

        return next(context, ct);
    }
}
