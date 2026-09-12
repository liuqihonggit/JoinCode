namespace Core.Agents;

/// <summary>
/// 权限路由中间件 — 启动 Leader 权限路由 + Plan 审批路由
/// 合并自路径 B 的 SpawnCoordPermissionRoutingMiddleware
/// 主代理保留（作为 Leader 需启动权限路由）
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PermissionRoutingMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public PermissionRoutingMiddleware(IMailbox messageBroker, ISubAgentContextAccessor subAgentContextAccessor, ILogger<PermissionRoutingMiddleware> logger, SwarmPermissionMessageRouter? permissionRouter = null, PlanApprovalMessageRouter? planApprovalRouter = null)
    {
        _messageBroker = messageBroker;
        _subAgentContextAccessor = subAgentContextAccessor;
        _logger = logger;
        _permissionRouter = permissionRouter;
        _planApprovalRouter = planApprovalRouter;
    }
    private readonly IMailbox _messageBroker;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ILogger<PermissionRoutingMiddleware> _logger;
    private readonly SwarmPermissionMessageRouter? _permissionRouter;
    private readonly PlanApprovalMessageRouter? _planApprovalRouter;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (context.Agent is null)
        {
            return next(context, ct);
        }

        EnsurePermissionRoutingStarted();
        context.PermissionRoutingEnsured = true;

        _planApprovalRouter?.StartTeammateRouting(context.AgentId);
        context.PlanApprovalRoutingStarted = true;

        return next(context, ct);
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

        _logger.LogInformation("[PermissionRouting] Leader 消息路由已启动: CoordinatorId={CoordinatorId}", coordinatorId);
    }
}
