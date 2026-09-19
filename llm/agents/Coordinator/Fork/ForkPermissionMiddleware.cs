namespace Core.Agents.Coordinator;

/// <summary>
/// Fork 权限同步中间件 — 同步子智能体权限到权限桥
/// </summary>
[Register(typeof(IForkMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ForkPermissionMiddleware : ServiceEntity, IForkMiddleware {

    /// <summary>
    /// 初始化 Fork 权限同步中间件
    /// </summary>
    /// <param name="permissionBridge">权限同步桥</param>
    /// <param name="logger">日志记录器</param>
    public ForkPermissionMiddleware(ISwarmPermissionBridge? permissionBridge = null, ILogger<ForkPermissionMiddleware>? logger = null) {
        _permissionBridge = permissionBridge;
        _logger = logger;
    }
    private readonly ISwarmPermissionBridge? _permissionBridge;
    private readonly ILogger<ForkPermissionMiddleware>? _logger;

    /// <summary>权限同步在 Spawn 之后</summary>

    /// <summary>权限同步失败不应中断管道</summary>
    /// <summary>
    /// 错误处理策略 — 权限同步失败时继续执行管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 异步执行权限同步逻辑，将 Fork 权限配置同步到权限桥
    /// </summary>
    /// <param name="context">Fork 上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ForkContext context, MiddlewareDelegate<ForkContext> next, CancellationToken ct) {
        if (_permissionBridge == null) {
            context.PermissionsSynced = false;
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        try {
            var request = new PermissionSyncRequest {
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
        } catch (Exception ex) {
            context.PermissionsSynced = false;
            _logger?.LogWarning(ex, "Failed to sync permissions for fork {ForkId}", context.ForkId);
        }

        await next(context, ct).ConfigureAwait(false);
    }
}