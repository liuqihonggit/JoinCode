namespace Core.Configuration;

/// <summary>
/// Hook 配置刷新中间件 — 对齐 TS 版 updateHooksConfigSnapshot()
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class HookRefreshMiddleware : ISettingsMiddleware
{
    [Inject] private readonly IHookConfigurationManager? _hookConfigurationManager;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        if (_hookConfigurationManager is not null)
        {
            await _hookConfigurationManager.InvalidateCacheAsync(ct).ConfigureAwait(false);
            context.Logger?.LogDebug("Hook 配置缓存已刷新");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
