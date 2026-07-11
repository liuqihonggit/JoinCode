namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordRecordContextMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly IClockService _clock;

    public Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        ctx.SpawnedAt = now;
        ctx.ExecutionContext = new AgentExecutionContext
        {
            AgentId = ctx.AgentId,
            Task = ctx.Task,
            SpawnedAt = now,
            RetryCount = 0
        };

        return next(ctx, ct);
    }
}
