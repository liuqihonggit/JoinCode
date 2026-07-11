namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IHandleWorkMiddleware))]
public sealed partial class WorkSessionTrackMiddleware : IHandleWorkMiddleware
{
    [Inject] private readonly ILogger<WorkSessionTrackMiddleware>? _logger;
    [Inject] private readonly IClockService _clock;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(HandleWorkContext ctx, MiddlewareDelegate<HandleWorkContext> next, CancellationToken ct)
    {
        var work = ctx.Work;
        var handle = ctx.Handle!;

        ctx.ActiveSessions![work.SessionId] = handle;
        ctx.SessionStartTimes![work.SessionId] = _clock.GetUtcNow();
        ctx.SessionWorkIds![work.SessionId] = work.WorkId;

        if (ctx.SessionIngressToken is not null)
        {
            ctx.SessionIngressTokens![work.SessionId] = ctx.SessionIngressToken;
        }

        if (ctx.UseCcrV2)
        {
            ctx.V2Sessions!.Add(work.SessionId);
        }

        var compatId = SessionIdCompat.ToCompatSessionId(work.SessionId);
        ctx.SessionCompatIds![work.SessionId] = compatId;

        _logger?.LogInformation("BridgeMain: session {SessionId} started, active={Active}/{Max}, ccrV2={CcrV2}",
            work.SessionId, ctx.ActiveSessions.Count, ctx.Config.MaxSessions, ctx.UseCcrV2);

        ctx.TelemetryCount!("tengu_bridge_session_started", new Dictionary<string, string>
        {
            ["active_sessions"] = ctx.ActiveSessions.Count.ToString(),
            ["spawn_mode"] = ctx.Config.SpawnMode.ToValue(),
            ["in_worktree"] = (ctx.SessionWorktrees!.ContainsKey(work.SessionId)).ToString(),
        });

        ctx.CapacityWake?.Invoke();

        return Task.CompletedTask;
    }
}
