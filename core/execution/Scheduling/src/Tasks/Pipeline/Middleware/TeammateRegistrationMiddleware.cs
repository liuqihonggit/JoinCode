namespace Core.Scheduling.Tasks;


[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateRegistrationMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    public TeammateRegistrationMiddleware(IMailbox messageBroker, ISubAgentContextAccessor subAgentContextAccessor, ILogger<TeammateRegistrationMiddleware>? logger = null, IMailboxPoller? mailboxPoller = null)
    {
        _messageBroker = messageBroker;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
        _mailboxPoller = mailboxPoller;
    }
    private readonly IMailbox _messageBroker;
    private readonly ILogger<TeammateRegistrationMiddleware>? _logger;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;


    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        var definition = ctx.Definition;

        var sessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
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
            using var guard = ctx.TeammateLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");
            ctx.ActiveTeammates[definition.TeammateId] = state;
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
