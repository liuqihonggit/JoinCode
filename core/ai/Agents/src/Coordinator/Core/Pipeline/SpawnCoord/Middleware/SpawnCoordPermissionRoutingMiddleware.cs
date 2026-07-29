namespace Core.Agents.Coordinator;

[Register(typeof(IAgentSpawnCoordMiddleware))]
public sealed partial class SpawnCoordPermissionRoutingMiddleware : IAgentSpawnCoordMiddleware
{
    [Inject] private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    [Inject] private readonly ILogger<SpawnCoordPermissionRoutingMiddleware> _logger;
    [Inject] private readonly SwarmPermissionMessageRouter? _permissionRouter;
    [Inject] private readonly PlanApprovalMessageRouter? _planApprovalRouter;

    public Task InvokeAsync(AgentSpawnCoordContext ctx, MiddlewareDelegate<AgentSpawnCoordContext> next, CancellationToken ct)
    {
        EnsurePermissionRoutingStarted();
        ctx.PermissionRoutingEnsured = true;

        _planApprovalRouter?.StartTeammateRouting(ctx.AgentId);
        ctx.PlanApprovalRoutingStarted = true;

        return next(ctx, ct);
    }

    private bool _permissionRoutingStarted;
    private void EnsurePermissionRoutingStarted()
    {
        if (_permissionRoutingStarted || _permissionRouter == null) return;

        var coordinatorId = _subAgentContextAccessor.Current?.AgentId ?? "coordinator";
        _messageBroker.RegisterAgent(coordinatorId);
        _permissionRouter.StartRouting(coordinatorId);

        _planApprovalRouter?.StartLeaderRouting(coordinatorId);

        _permissionRoutingStarted = true;

        _logger.LogInformation("[AgentCoordinator] Leader 消息路由已启动: CoordinatorId={CoordinatorId}", coordinatorId);
    }
}
