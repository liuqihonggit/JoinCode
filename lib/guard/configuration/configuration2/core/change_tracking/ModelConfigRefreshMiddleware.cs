
namespace Core.Configuration;

/// <summary>
/// 模型配置刷新中间件 — settings.json 变更时将 Vendor 数据灌入 ModelConfigLoader
/// 数据流：settings.json → SettingsReloadMiddleware → VendorModelMapper → ModelConfigLoader.ApplyProviders
/// </summary>
[Register(typeof(ISettingsMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ModelConfigRefreshMiddleware : ServiceEntity, ISettingsMiddleware
{
    private readonly IModelConfigLoader _modelConfigLoader;

    public ModelConfigRefreshMiddleware(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(SettingsContext context, MiddlewareDelegate<SettingsContext> next, CancellationToken ct)
    {
        if (context.NewSettings is not null)
        {
            var providers = VendorModelMapper.BuildProviders(context.NewSettings);
            _modelConfigLoader.ApplyProviders(providers);
            context.Logger?.LogInformation("模型配置已从 settings.json 刷新");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
