namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 7: 验证 Provider 配置 — Provider 必须有 API Key
/// </summary>
[Register(typeof(IConfigLoadMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ProviderValidationMiddleware : ServiceEntity, IConfigLoadMiddleware {
    private readonly IProviderDefinitionRegistry _registry;

    /// <summary>
    /// 构造供应商验证中间件
    /// </summary>
    public ProviderValidationMiddleware(IProviderDefinitionRegistry registry) {
        _registry = registry;
    }


    /// <summary>
    /// 执行中间件 — 验证当前 Provider 配置是否有效（如必须包含 API Key），通过后调用下一管道
    /// </summary>
    public Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct) {
        // 元命令模式跳过验证 — context.SkipProviderValidation 由 ConfigLoader.SkipProviderValidation 传入
        if (context.SkipProviderValidation) {
            context.Result = context.Config;
            return next(context, ct);
        }

        var config = context.Config;
        var definition = _registry.TryGet(config.Provider.Vendor);
        if (definition is not null && !definition.IsValid(config.Provider)) {
            throw new ConfigurationException(
                $"Provider '{config.Provider.Vendor}' 配置无效: 缺少 API Key。" +
                $"请设置环境变量 {definition.ApiKeyEnvironmentVariable ?? "供应商专属变量"}" +
                $" 或在 {AppDataConstants.Paths.AuthFilePath} 中添加 '{config.Provider.Vendor}' 键。");
        }

        context.Result = config;

        return next(context, ct);
    }
}