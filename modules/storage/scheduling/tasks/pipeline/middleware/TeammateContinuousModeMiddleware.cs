namespace Core.Scheduling.Tasks;


/// <summary>
/// Teammate 连续模式中间件 — 当定义启用连续模式时，启动运行循环并标记已处理
/// </summary>
[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateContinuousModeMiddleware : ServiceEntity, ITeammateExecutionMiddleware {

    /// <summary>
    /// 初始化 Teammate 连续模式中间件
    /// </summary>
    /// <param name="clock">时钟服务</param>
    /// <param name="logger">日志记录器</param>
    public TeammateContinuousModeMiddleware(IClockService clock, ILogger<TeammateContinuousModeMiddleware>? logger = null) {
        _clock = clock;
        _logger = logger;
    }
    private readonly ILogger<TeammateContinuousModeMiddleware>? _logger;
    private readonly IClockService _clock;


    /// <inheritdoc/>
    public Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct) {
        if (!ctx.Definition.ContinuousMode) {
            return next(ctx, ct);
        }

        if (ctx.RunLoopAsync is not null && ctx.LifecycleCts is not null) {
            _ = ctx.RunLoopAsync(ctx.Definition, ctx.State ?? throw new InvalidOperationException("Teammate state is not available."), ctx.LifecycleCts.Token);
        }

        var elapsed = (long)(_clock.GetUtcNow() - ctx.StartTime).TotalMilliseconds;
        ctx.Result = AgentTaskResult.Success(
            ctx.Definition.TaskId,
            ctx.Definition.TeammateId,
            "Teammate started in continuous mode",
            elapsed);
        ctx.ContinuousModeHandled = true;

        return Task.CompletedTask;
    }
}