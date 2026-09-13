namespace Core.Bridge;

/// <summary>
/// 会话恢复中间件 — 处理 --continue 与 --session-id 参数,从指针或 API 恢复会话上下文
/// </summary>
[Register(typeof(IBridgeRunMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RunResumeMiddleware : ServiceEntity, IBridgeRunMiddleware
{

    /// <summary>
    /// 构造会话恢复中间件
    /// </summary>
    /// <param name="deps">桥接主依赖集合</param>
    /// <param name="logger">日志记录器</param>
    public RunResumeMiddleware(BridgeMainDeps deps, ILogger<RunResumeMiddleware> logger)
    {
        _deps = deps;
        _logger = logger;
    }
    private readonly BridgeMainDeps _deps;
    private readonly ILogger<RunResumeMiddleware> _logger;

    /// <summary>
    /// 执行中间件 — 根据 --continue 或 --session-id 恢复会话,填充上下文恢复字段
    /// </summary>
    /// <param name="ctx">桥接运行上下文</param>
    /// <param name="next">后续中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task InvokeAsync(BridgeRunContext ctx, MiddlewareDelegate<BridgeRunContext> next, CancellationToken ct)
    {
        if (ctx.Args.ContinueSession)
        {
            var found = await _deps.PointerService.ReadAcrossWorktreesAsync(
                _deps.WorkingDirectory, ct).ConfigureAwait(false);
            if (found is not null)
            {
                var (pointerWithAge, pointerDir) = found.Value;
                ctx.ResumeSessionId = pointerWithAge.Pointer.SessionId;
                ctx.ReuseEnvironmentId = pointerWithAge.Pointer.EnvironmentId;
                ctx.ResumePointerDir = pointerDir;
                var ageMin = Math.Round(pointerWithAge.AgeMs / 60_000.0);
                var ageStr = ageMin < 60 ? $"{ageMin}m" : $"{Math.Round(ageMin / 60.0)}h";
                var fromWt = pointerDir != _deps.WorkingDirectory ? $" from worktree {pointerDir}" : "";
                _logger.LogInformation("BridgeMain: resuming session {SessionId} ({Age} ago){FromWt}",
                    ctx.ResumeSessionId, ageStr, fromWt);
            }
            else
            {
                _logger.LogDebug("BridgeMain: --continue but no valid pointer found in this directory or its worktrees");
            }
        }
        else if (ctx.Args.SessionId is not null)
        {
            ctx.ResumeSessionId = ctx.Args.SessionId;
            try
            {
                var envId = await _deps.ApiClient.GetBridgeSessionEnvironmentIdAsync(
                    ctx.ResumeSessionId, ct).ConfigureAwait(false);
                if (envId is not null)
                {
                    ctx.ReuseEnvironmentId = envId;
                    _logger.LogInformation("BridgeMain: resuming session {SessionId} on environment {EnvId}",
                        ctx.ResumeSessionId, envId);
                }
                else
                {
                    _logger.LogDebug("BridgeMain: session {SessionId} has no environment_id, will register fresh", ctx.ResumeSessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "BridgeMain: getBridgeSession failed for {SessionId} (non-fatal)", ctx.ResumeSessionId);
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
