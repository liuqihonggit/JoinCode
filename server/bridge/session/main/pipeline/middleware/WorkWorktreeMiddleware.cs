namespace Core.Bridge;


/// <summary>
/// 工作项 worktree 中间件 — 在 Worktree 模式下为会话创建独立工作树
/// </summary>
[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkWorktreeMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    /// <summary>
    /// 构造 worktree 中间件
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="worktreeService">可选的 worktree 服务</param>
    public WorkWorktreeMiddleware(ILogger<WorkWorktreeMiddleware>? logger = null, IAgentWorktreeService? worktreeService = null)
    {
        _logger = logger;
        _worktreeService = worktreeService;
    }
    private readonly ILogger<WorkWorktreeMiddleware>? _logger;
    private readonly IAgentWorktreeService? _worktreeService;

    /// <summary>错误行为 — 继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — Worktree 模式下创建工作树并注入上下文，失败则终止工作项
    /// </summary>
    /// <param name="ctx">工作处理上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        if (ctx.Config.SpawnMode != BridgeSpawnMode.Worktree || _worktreeService is null)
        {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var worktreeResult = await _worktreeService.CreateAgentWorktreeAsync(
                ctx.Work.SessionId,
                ctx.Config.Dir,
                cancellationToken: ct).ConfigureAwait(false);

            if (worktreeResult.Success && worktreeResult.Session?.WorktreePath is not null)
            {
                ctx.CreatedWorktreePath = worktreeResult.Session.WorktreePath;
                ctx.SessionWorktrees[ctx.Work.SessionId] = worktreeResult.Session.WorktreePath;
                _logger?.LogInformation("BridgeMain: created worktree for session {SessionId} at {Path}",
                    ctx.Work.SessionId, worktreeResult.Session.WorktreePath);
            }
            else
            {
                _logger?.LogError("BridgeMain: worktree creation failed for session {SessionId}, stopping work",
                    ctx.Work.SessionId);
                ctx.FailWork(ct);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: worktree creation error for session {SessionId}, stopping work",
                ctx.Work.SessionId);
            ctx.FailWork(ct);
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
