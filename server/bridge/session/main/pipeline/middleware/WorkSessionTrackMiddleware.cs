namespace Core.Bridge;


/// <summary>
/// 工作会话跟踪中间件 — 将新会话注册到跟踪集合，记录启动时间、工作 ID、ingress token、V2 标记与兼容 ID
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkSessionTrackMiddleware : ServiceEntity, IHandleWorkMiddleware {

    /// <summary>
    /// 构造 WorkSessionTrack 中间件
    /// </summary>
    /// <param name="clock">时钟服务，用于记录会话启动时间</param>
    /// <param name="logger">可选日志记录器</param>
    public WorkSessionTrackMiddleware(IClockService clock, ILogger<WorkSessionTrackMiddleware>? logger = null) {
        _clock = clock;
        _logger = logger;
    }
    private readonly ILogger<WorkSessionTrackMiddleware>? _logger;
    private readonly IClockService _clock;


    /// <summary>
    /// 执行会话跟踪中间件 — 注册会话句柄、记录元数据、触发遥测计数与容量唤醒，不调用 next（终端中间件）
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">管道下一个委托（本中间件不调用）</param>
    /// <param name="ct">取消令牌</param>
    public Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct) {
        var work = ctx.Work;
        var handle = ctx.Handle ?? throw new InvalidOperationException("Handle is not set. Ensure SpawnSubprocessMiddleware runs before WorkSessionTrackMiddleware.");

        var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
        ctx.Tracker.Sessions.Register(work.SessionId, new BridgeSessionState {
            Handle = handle,
            StartTime = _clock.GetUtcNow(),
            WorkId = work.WorkId,
            IngressToken = ctx.SessionIngressToken,
            WorktreePath = ctx.CreatedWorktreePath,
            CompatId = compatId,
            IsV2 = ctx.UseCcrV2,
        });

        _logger?.LogInformation("BridgeMain: session {SessionId} started, active={Active}/{Max}, ccrV2={CcrV2}",
            work.SessionId, ctx.Tracker.Sessions.Count, ctx.Config.MaxSessions, ctx.UseCcrV2);

        ctx.TelemetryCount?.Invoke("tengu_bridge_session_started", new Dictionary<string, string> {
            ["active_sessions"] = ctx.Tracker.Sessions.Count.ToString(),
            ["spawn_mode"] = ctx.Config.SpawnMode.ToValue(),
            ["in_worktree"] = (ctx.CreatedWorktreePath is not null).ToString(),
        });

        ctx.CapacityWake?.Invoke();

        return Task.CompletedTask;
    }
}