namespace Core.Bridge;


[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkSpawnMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    public WorkSpawnMiddleware(ILogger<WorkSpawnMiddleware>? logger = null, IAgentWorktreeService? worktreeService = null)
    {
        _logger = logger;
        _worktreeService = worktreeService;
    }
    private readonly ILogger<WorkSpawnMiddleware>? _logger;
    private readonly IAgentWorktreeService? _worktreeService;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        var accessTokenForSpawn = ctx.SessionIngressToken ?? ctx.GetAccessToken?.Invoke();
        var spawnDir = ctx.CreatedWorktreePath ?? ctx.SpawnDir ?? ctx.Config.Dir;

        var spawnOptions = new BridgeSubprocessOptions
        {
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

        try
        {
            ctx.Handle = await (ctx.Spawner ?? throw new InvalidOperationException("Spawner is not set.")).SpawnAsync(spawnOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: spawn failed for session {SessionId}", ctx.Work.SessionId);

            if (ctx.CreatedWorktreePath is not null && _worktreeService is not null)
            {
                try
                {
                    await _worktreeService.RemoveAgentWorktreeAsync(
                        ctx.Work.SessionId, force: true, cancellationToken: ct).ConfigureAwait(false);
                    ctx.SessionWorktrees.TryRemove(ctx.Work.SessionId, out _);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogDebug(cleanupEx, "BridgeMain: worktree cleanup after spawn failure for {SessionId} (non-fatal)", ctx.Work.SessionId);
                }
            }

            ctx.CompletedWorkIds.TryAdd(ctx.Work.WorkId, 0);
            if (ctx.StopWorkAsync is not null)
            {
                await ctx.StopWorkAsync(ctx.Work.WorkId, ct).ConfigureAwait(false);
            }
            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
