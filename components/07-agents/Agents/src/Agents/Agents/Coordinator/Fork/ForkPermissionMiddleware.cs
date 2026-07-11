namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 权限同步中间件 — 同步子智能体权限到权限桥
/// </summary>
[Register(typeof(IForkMiddleware))]
public sealed partial class ForkPermissionMiddleware : IForkMiddleware
{
    [Inject] private readonly ISwarmPermissionBridge? _permissionBridge;
    [Inject] private readonly ILogger<ForkPermissionMiddleware>? _logger;

    /// <summary>权限同步在 Spawn 之后</summary>

    /// <summary>权限同步失败不应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct)
    {
        if (_permissionBridge == null)
        {
            context.PermissionsSynced = false;
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var request = new PermissionSyncRequest
            {
                AgentId = context.ForkId,
                CoordinatorId = context.Options.ParentSessionId,
                Mode = context.Options.PermissionMode,
                AllowedTools = context.Options.AllowedTools,
                DeniedTools = context.Options.DeniedTools
            };

            await _permissionBridge.SyncPermissionsAsync(context.ForkId, request, ct).ConfigureAwait(false);
            context.PermissionsSynced = true;

            _logger?.LogDebug("Fork {ForkId} permissions synced: Mode={Mode}, Allowed={AllowedCount}, Denied={DeniedCount}",
                context.ForkId, context.Options.PermissionMode,
                context.Options.AllowedTools?.Count ?? 0,
                context.Options.DeniedTools?.Count ?? 0);
        }
        catch (Exception ex)
        {
            context.PermissionsSynced = false;
            _logger?.LogWarning(ex, "Failed to sync permissions for fork {ForkId}", context.ForkId);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
