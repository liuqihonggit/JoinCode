
namespace Api;

using Api.LLM.QueryServices;

public static class ApiRegistration
{
    private static readonly QueryServiceFactory s_factory = new();

    public static IServiceCollection AddSKKernelAdapter(
        this IServiceCollection services,
        ProviderConfig providerConfig)
    {
        services.AddSingleton<IQueryService>(sp => CreateQueryService(sp, providerConfig));

        return services;
    }

    public static IServiceCollection AddSKKernelAdapterWithCustomService(
        this IServiceCollection services,
        IQueryService customService)
    {
        services.AddSingleton(customService);

        return services;
    }

    public static IChatClient CreateEmptyKernel()
    {
        // 空 Kernel 仅用于测试场景（Agents 单元测试等）— 不发起真实 API 调用
        // 使用 EmptyQueryService 桩实现避免 QueryServiceBase 强制要求 IProviderDefinition
        // 真实场景请通过 AddKernelWithPlugins / AddSKKernelAdapter 注册真实 ProviderConfig
        return new ChatClient(new EmptyQueryService());
    }

    /// <summary>
    /// 注册 Pipe 查询服务（通过命名管道与外部 LLM 服务通信）
    /// </summary>
    public static IServiceCollection AddPipeQueryService(
        this IServiceCollection services,
        PipeTransportConfig config,
        string? apiKey = null)
    {
        services.AddSingleton(config);
        services.AddSingleton<IQueryService>(sp =>
        {
            var logger = sp.GetService<ILogger<Chat.PipeQueryService>>();
            return new Chat.PipeQueryService(config, apiKey, logger);
        });

        return services;
    }

    /// <summary>
    /// 注册 Kernel 及其插件（支持 Pipe 端点）
    /// </summary>
    public static IServiceCollection AddKernelWithPlugins(
        this IServiceCollection services,
        ProviderConfig providerConfig,
        PipeTransportConfig? pipeEndpoint = null)
    {
        if (pipeEndpoint != null)
        {
            services.AddPipeQueryService(pipeEndpoint, providerConfig.ApiKey);
        }
        else
        {
            services.AddSingleton<IQueryService>(sp => CreateQueryService(sp, providerConfig));
        }

        return services;
    }

    /// <summary>
    /// 注册 Kernel 及其动态插件
    /// </summary>
    public static IServiceCollection AddKernelWithDynamicPlugins(
        this IServiceCollection services,
        ProviderConfig providerConfig)
    {
        return services;
    }

    /// <summary>
    /// 从 DI 容器解析依赖并通过 QueryServiceFactory 创建 IQueryService — 统一工厂入口
    /// 工厂按 ProviderKind 分派到 OpenAIQueryService / AzureQueryService / AnthropicQueryService 三个派生类
    /// </summary>
    private static IQueryService CreateQueryService(IServiceProvider sp, ProviderConfig providerConfig)
    {
        var logger = sp.GetService<ILogger<IQueryService>>();
        var fs = sp.GetService<IFileSystem>();
        return s_factory.Create(providerConfig, logger: logger, fileSystem: fs);
    }
}

[Register(typeof(IToolGroupFactory))]
public sealed class ToolGroupFactory : IToolGroupFactory
{
    public IToolGroup CreateFromObject(object instance, string pluginName)
    {
        throw new NotSupportedException(
            "CreateFromObject 不再支持反射扫描。请使用 McpToolBridge.CreatePluginAsync 或手动构建 ToolGroup。");
    }
}
