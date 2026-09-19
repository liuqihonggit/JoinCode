namespace Core.Configuration;

/// <summary>
/// Hook 配置刷新中间件 — 对齐 TS 版 updateHooksConfigSnapshot()
/// </summary>
[Register(typeof(ISettingsMiddleware), ServiceLifetime.Singleton)]
public sealed partial class HookRefreshMiddleware : ServiceEntity, ISettingsMiddleware {

    /// <summary>
    /// 构造函数 — 注入可选的钩子配置管理器
    /// </summary>
    /// <param name="hookConfigurationManager">可选的钩子配置管理器</param>
    public HookRefreshMiddleware(IHookConfigurationManager? hookConfigurationManager = null) {
        _hookConfigurationManager = hookConfigurationManager;
    }
    private readonly IHookConfigurationManager? _hookConfigurationManager;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct) {
        if (_hookConfigurationManager is not null) {
            await _hookConfigurationManager.InvalidateCacheAsync(ct).ConfigureAwait(false);
            context.Logger?.LogDebug("Hook 配置缓存已刷新");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}