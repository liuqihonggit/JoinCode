namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordRegisterMessageMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ILogger<SpawnCoordRegisterMessageMiddleware> _logger;
    [Inject] private readonly ITeammateInitService? _teammateInitService;
    [Inject] private readonly IServiceProvider? _serviceProvider;

    private ITeammateInitService? ResolvedTeammateInitService => _teammateInitService ?? _serviceProvider?.GetService(typeof(ITeammateInitService)) as ITeammateInitService;

    public async Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        try
        {
            var sessionId = _subAgentContextAccessor.Current?.SessionId;
            ctx.SessionId = sessionId;
            _messageBroker.RegisterAgent(ctx.AgentId, sessionId);

            await InitializeTeammateHooksIfNeededAsync(ctx.AgentId, sessionId, ctx.CancellationToken).ConfigureAwait(false);
            ctx.MessageRegistered = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AgentCoordinator] 注册Agent {AgentId} 消息通道时发生异常", ctx.AgentId);
        }

        await next(ctx, ct).ConfigureAwait(false);
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
            _logger.LogWarning(ex, "[AgentCoordinator] 初始化 Teammate {AgentId} 钩子失败", agentId);
        }
    }
}
