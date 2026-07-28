namespace Core.Bridge;

[Register(typeof(IBridgeRunMiddleware))]
public sealed partial class RunResumeMiddleware : IBridgeRunMiddleware
{
    [Inject] private readonly BridgeMainDeps _deps;
    [Inject] private readonly ILogger<RunResumeMiddleware> _logger;

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
