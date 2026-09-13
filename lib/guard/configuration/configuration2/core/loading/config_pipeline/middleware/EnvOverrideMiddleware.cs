
namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 3: 用环境变量覆盖配置 — 环境变量优先级高于 settings.json
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class EnvOverrideMiddleware : ServiceEntity, IConfigLoadMiddleware
{
    private readonly SettingsMapper _mapper;

    /// <summary>
    /// 初始化环境变量覆盖中间件
    /// </summary>
    /// <param name="mapper">设置映射器</param>
    public EnvOverrideMiddleware(SettingsMapper mapper)
    {
        _mapper = mapper;
    }


    /// <inheritdoc />
    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        _mapper.ApplyEnvOverrides(context.Config, context.Settings);

        return next(context, ct);
    }
}
