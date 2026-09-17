
namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// 配置映射中间件 — 将 ConfigLoadContext.Settings 通过 SettingsMapper 转换为 WorkflowConfig 并写入上下文
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ConfigMapMiddleware : ServiceEntity, IConfigLoadMiddleware
{
    private readonly SettingsMapper _mapper;

    /// <summary>
    /// 构造配置映射中间件
    /// </summary>
    /// <param name="mapper">Settings 映射器,用于将 SettingsJson 转换为 WorkflowConfig</param>
    public ConfigMapMiddleware(SettingsMapper mapper)
    {
        _mapper = mapper;
    }


    /// <inheritdoc/>
    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        _mapper.SkipProviderValidation = context.SkipProviderValidation;
        context.Config = _mapper.ToWorkflowConfig(context.Settings);

        return next(context, ct);
    }
}
