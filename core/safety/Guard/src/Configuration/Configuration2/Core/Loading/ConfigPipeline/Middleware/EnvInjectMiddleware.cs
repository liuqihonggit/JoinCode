namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 2: 注入 settings.env 到环境变量
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class EnvInjectMiddleware : ServiceEntity, IConfigLoadMiddleware
{

    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        SettingsMapper.InjectEnvFromSettings(context.Settings);

        return next(context, ct);
    }
}
