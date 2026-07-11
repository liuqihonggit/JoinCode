namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkWorktreeMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkWorktreeMiddleware>? _logger;
    [Inject] private readonly IAgentWorktreeService? _worktreeService;

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
                ctx.SessionWorktrees![ctx.Work.SessionId] = worktreeResult.Session.WorktreePath;
                _logger?.LogInformation("BridgeMain: created worktree for session {SessionId} at {Path}",
                    ctx.Work.SessionId, worktreeResult.Session.WorktreePath);
            }
            else
            {
                _logger?.LogError("BridgeMain: worktree creation failed for session {SessionId}, stopping work",
                    ctx.Work.SessionId);
                ctx.CompletedWorkIds!.Add(ctx.Work.WorkId);
                await SafeStopWorkAsync(ctx, ct).ConfigureAwait(false);
                ctx.ShortCircuited = true;
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BridgeMain: worktree creation error for session {SessionId}, stopping work",
                ctx.Work.SessionId);
            ctx.CompletedWorkIds!.Add(ctx.Work.WorkId);
            await SafeStopWorkAsync(ctx, ct).ConfigureAwait(false);
            ctx.ShortCircuited = true;
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }

    private static async Task SafeStopWorkAsync(HandleWorkContext ctx, CancellationToken ct)
    {
        if (ctx.StopWorkAsync is not null)
        {
            await ctx.StopWorkAsync(ctx.Work.WorkId, ct).ConfigureAwait(false);
        }
    }
}
