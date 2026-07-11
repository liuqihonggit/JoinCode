namespace Core.Configuration;

/// <summary>
/// 设置重载中间件 — 重新加载 SettingsJson，对齐 TS 版 getInitialSettings()
/// </summary>
[Register(typeof(ISettingsMiddleware))]
public sealed partial class SettingsReloadMiddleware : ISettingsMiddleware
{
    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public async Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        var newSettings = await ConfigLoader.LoadSettingsJsonAsync(context.FileSystem, ct).ConfigureAwait(false);
        context.NewSettings = newSettings;

        context.Logger?.LogInformation("设置已重新加载");

        await next(context, ct).ConfigureAwait(false);
    }
}
