namespace Core.Configuration;

/// <summary>
/// 权限缓存清除中间件 — 对齐 TS: IToolPermissionManager.ClearCache
/// settings.json 中的 permissions 规则变更后需要热同步
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class PermissionCacheMiddleware : ISettingsMiddleware
{
    [Inject] private readonly IToolPermissionManager? _toolPermissionManager;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        _toolPermissionManager?.ClearCache();
        if (_toolPermissionManager is not null)
        {
            context.Logger?.LogDebug("权限规则缓存已清除");
        }

        return next(context, ct);
    }
}
