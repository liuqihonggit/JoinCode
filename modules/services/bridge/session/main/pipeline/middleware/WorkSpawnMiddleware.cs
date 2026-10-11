namespace Core.Bridge;


/// <summary>
/// 工作生成中间件 — 负责生成子进程处理工作项，失败时清理 worktree 并终止工作
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkSpawnMiddleware : ServiceEntity, IHandleWorkMiddleware {

    /// <summary>
    /// 构造工作生成中间件
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="worktreeService">Agent worktree 服务（可选）</param>
    public WorkSpawnMiddleware(ILogger<WorkSpawnMiddleware>? logger = null, IAgentWorktreeService? worktreeService = null) {
        _logger = logger;
        _worktreeService = worktreeService;
    }
    private readonly ILogger<WorkSpawnMiddleware>? _logger;
    private readonly IAgentWorktreeService? _worktreeService;

    /// <summary>错误行为：继续执行</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行子进程生成 — 构建 spawn 选项并调用 Spawner，失败时回滚 worktree 并标记短路
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct) {
        var accessTokenForSpawn = ctx.SessionIngressToken ?? ctx.GetAccessToken?.Invoke();
        var spawnDir = ctx.CreatedWorktreePath ?? ctx.SpawnDir ?? ctx.Config.Dir;

        var spawnOptions = new BridgeSubprocessOptions {
            SessionId = ctx.Work.SessionId,
            SdkUrl = ctx.SdkUrl ?? throw new InvalidOperationException("SdkUrl is not set. Ensure CcrV2RegisterMiddleware runs before SpawnMiddleware."),
            AccessToken = accessTokenForSpawn,
            Dir = spawnDir,
            DebugLog = ctx.Config.DebugLog,
            Sandbox = ctx.Config.Sandbox,
            DebugFile = ctx.Config.DebugFile,
            PermissionMode = ctx.PermissionMode,
            UseCcrV2 = ctx.UseCcrV2,
            WorkerEpoch = ctx.WorkerEpoch,
            OnPermissionRequest = ctx.OnPermissionRequest,
            OnActivity = ctx.OnActivity,
            OnFirstUserMessage = ctx.OnFirstUserMessage,
        };

        try {
            ctx.Handle = await (ctx.Spawner ?? throw new InvalidOperationException("Spawner is not set.")).SpawnAsync(spawnOptions, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "BridgeMain: spawn failed for session {SessionId}", ctx.Work.SessionId);

            if (ctx.CreatedWorktreePath is not null && _worktreeService is not null) {
                try {
                    await _worktreeService.RemoveAgentWorktreeAsync(
                        ctx.Work.SessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                } catch (Exception cleanupEx) {
                    _logger?.LogDebug(cleanupEx, "BridgeMain: worktree cleanup after spawn failure for {SessionId} (non-fatal)", ctx.Work.SessionId);
                }
            }

            ctx.Tracker.WorkCompletion.Mark(ctx.Work.WorkId);
            if (ctx.StopWorkAsync is not null) {
                await ctx.StopWorkAsync(ctx.Work.WorkId, ct).ConfigureAwait(false);
            }
            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}