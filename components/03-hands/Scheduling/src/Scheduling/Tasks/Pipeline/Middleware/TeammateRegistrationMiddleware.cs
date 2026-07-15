namespace Core.Scheduling.Tasks;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(ITeammateExecutionMiddleware))]
public sealed partial class TeammateRegistrationMiddleware : ITeammateExecutionMiddleware
{
    [Inject] private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ILogger<TeammateRegistrationMiddleware>? _logger;
    [Inject] private readonly IMailboxPoller? _mailboxPoller;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        var definition = ctx.Definition;

        var sessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? "default";
        _messageBroker.RegisterAgent(definition.TeammateId, sessionId);

        StartMailboxPollingIfNeeded(definition.TeammateId);

        var lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var teammateContext = new TeammateContext
        {
            AgentId = definition.TeammateId,
            AgentName = definition.TeammateId,
            TeamName = definition.TeamName ?? "default",
            TeamId = definition.TeamId,
            Color = definition.Color,
            PlanModeRequired = definition.PlanModeRequired,
            ParentSessionId = definition.ParentSessionId ?? sessionId,
            IsInProcess = true
        };

        var state = new TeammateState
        {
            Agent = ctx.Agent ?? throw new InvalidOperationException("Agent is not set."),
            LifecycleCts = lifecycleCts,
            Context = teammateContext,
            IsIdle = false
        };

        if (ctx.TeammateLock is not null)
        {
            await ctx.TeammateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ctx.ActiveTeammates[definition.TeammateId] = state;
            }
            finally
            {
                ctx.TeammateLock.Release();
            }
        }

        ctx.PendingMessages[definition.TeammateId] = Channel.CreateUnbounded<CoordinatorMessage>();

        ctx.State = state;
        ctx.LifecycleCts = lifecycleCts;

        await next(ctx, ct).ConfigureAwait(false);
    }

    private void StartMailboxPollingIfNeeded(string teammateId)
    {
        if (_mailboxPoller == null) return;

        var sessionId = _messageBroker.GetSessionId(teammateId);
        if (sessionId is null) return;

        try
        {
            _mailboxPoller.StartPolling(teammateId, sessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start mailbox polling for teammate {TeammateId}", teammateId);
        }
    }
}
