namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class WorkWorktreeMiddleware : ServiceEntity, IHandleWorkMiddleware
{

    public WorkWorktreeMiddleware(ILogger<WorkWorktreeMiddleware>? logger = null, IAgentWorktreeService? worktreeService = null)
    {
        _logger = logger;
        _worktreeService = worktreeService;
    }
    private readonly ILogger<WorkWorktreeMiddleware>? _logger;
    private readonly IAgentWorktreeService? _worktreeService;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

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
