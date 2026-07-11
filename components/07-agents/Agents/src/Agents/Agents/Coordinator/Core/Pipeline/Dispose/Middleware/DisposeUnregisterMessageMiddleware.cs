namespace Core.Agents.Coordinator;

[Register(typeof(IAgentDisposeMiddleware))]
public sealed partial class DisposeUnregisterMessageMiddleware : IAgentDisposeMiddleware
{
    [Inject] private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ILogger<DisposeUnregisterMessageMiddleware> _logger;

    public async Task InvokeAsync(AgentDisposeContext ctx, MiddlewareDelegate<AgentDisposeContext> next, CancellationToken ct)
    {
        try
        {
            _messageBroker.UnregisterAgent(ctx.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentCoordinator] 注销Agent {AgentId} 消息通道时发生异常", ctx.AgentId);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
