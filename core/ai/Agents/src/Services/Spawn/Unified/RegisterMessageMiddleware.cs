namespace Core.Agents;

/// <summary>
/// 注册消息通道中间件 — 注册 Agent 消息通道 + 初始化 Teammate 钩子
/// 合并自路径 B 的 SpawnCoordRegisterMessageMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class RegisterMessageMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public RegisterMessageMiddleware(IMailbox messageBroker, ISubAgentContextAccessor subAgentContextAccessor, ILogger<RegisterMessageMiddleware> logger, ITeammateInitService? teammateInitService = null, IServiceProvider? serviceProvider = null)
    {
        _messageBroker = messageBroker;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
        _teammateInitService = teammateInitService;
        _serviceProvider = serviceProvider;
    }
    private readonly IMailbox _messageBroker;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ILogger<RegisterMessageMiddleware> _logger;
    private readonly ITeammateInitService? _teammateInitService;
    private readonly IServiceProvider? _serviceProvider;

    private ITeammateInitService? ResolvedTeammateInitService => _teammateInitService ?? _serviceProvider?.GetService(typeof(ITeammateInitService)) as ITeammateInitService;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.IsMainAgent || context.Agent is null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var sessionId = _subAgentContextAccessor.Current?.SessionId;
            context.SessionId = sessionId;
            _messageBroker.RegisterAgent(context.AgentId, sessionId);

            await InitializeTeammateHooksIfNeededAsync(context.AgentId, sessionId, context.CancellationToken).ConfigureAwait(false);
            context.MessageRegistered = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RegisterMessage] 注册Agent {AgentId} 消息通道时发生异常", context.AgentId);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task InitializeTeammateHooksIfNeededAsync(string agentId, string? sessionId, CancellationToken cancellationToken)
    {
        if (ResolvedTeammateInitService is null || sessionId is null) return;

        var teamId = _subAgentContextAccessor.Current?.TeamId;
        if (string.IsNullOrEmpty(teamId)) return;

        try
        {
            await ResolvedTeammateInitService.InitializeTeammateHooksAsync(teamId, agentId, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RegisterMessage] 初始化 Teammate {AgentId} 钩子失败", agentId);
        }
    }
}
