namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 4: 环境变量覆盖（Provider/Model/Endpoint 等，不含 API Key）
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class EnvOverrideMiddleware : IConfigLoadMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        SettingsMapper.ApplyEnvOverrides(context.Config);

        return next(context, ct);
    }
}
