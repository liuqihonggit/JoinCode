namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 7: 验证 Provider 配置 — Provider 必须有 API Key
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class ProviderValidationMiddleware : IConfigLoadMiddleware
{
    private readonly IProviderDefinitionRegistry _registry;

    public ProviderValidationMiddleware(IProviderDefinitionRegistry registry)
    {
        _registry = registry;
    }


    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var config = context.Config;
        var definition = _registry.TryGet(config.Provider.Provider);
        if (definition is not null && !definition.IsValid(config.Provider))
        {
            throw new ConfigurationException(
                $"Provider '{config.Provider.Provider}' 配置无效: 缺少 API Key。" +
                $"请设置环境变量 {definition.ApiKeyEnvironmentVariable ?? "JCC_API_KEY"}" +
                $" 或在 {WorkflowConstants.Paths.AuthFilePath} 中添加 '{config.Provider.Provider}' 键。");
        }

        context.Result = config;

        return next(context, ct);
    }
}
