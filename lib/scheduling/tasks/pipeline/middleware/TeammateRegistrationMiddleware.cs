namespace Core.Scheduling.Tasks;


/// <summary>
/// Teammate 注册中间件 — 向消息邮箱注册 Teammate、启动邮箱轮询并建立 Teammate 运行时状态
/// </summary>
[Register(typeof(ITeammateExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class TeammateRegistrationMiddleware : ServiceEntity, ITeammateExecutionMiddleware
{

    /// <summary>
    /// 初始化 Teammate 注册中间件
    /// </summary>
    /// <param name="messageBroker">消息邮箱</param>
    /// <param name="subAgentContextAccessor">子智能体上下文访问器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="mailboxPoller">邮箱轮询器，为 null 时不启动轮询</param>
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


    /// <inheritdoc/>
    public async Task InvokeAsync(TeammateExecutionContext ctx, MiddlewareDelegate<TeammateExecutionContext> next, CancellationToken ct)
    {
        var definition = ctx.Definition;

        var sessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        _messageBroker.RegisterAgent(definition.TeammateId, sessionId);

        StartMailboxPollingIfNeeded(definition.TeammateId);

        var lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var teammateMeta = new TeammateMeta
        {
            AgentName = definition.TeammateId,
            TeamName = definition.TeamName ?? "default",
            Color = definition.Color,
            PlanModeRequired = definition.PlanModeRequired,
            ParentSessionId = definition.ParentSessionId ?? sessionId,
            IsInProcess = true
        };

        var state = new TeammateState
        {
            Agent = ctx.Agent ?? throw new InvalidOperationException("Agent is not set."),
            LifecycleCts = lifecycleCts,
            TeammateMeta = teammateMeta,
            IsIdle = false
        };

        ctx.ActiveTeammates[definition.TeammateId] = state;

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
