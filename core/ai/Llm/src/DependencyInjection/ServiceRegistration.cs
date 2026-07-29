namespace JoinCode.Llm.DependencyInjection;

using Api.LLM.QueryServices;

public static partial class ServiceRegistration
{
    private static readonly QueryServiceFactory s_factory = new();

    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        ProviderConfig providerConfig)
    {
        services.AddSingleton<IQueryService>(sp => CreateQueryService(sp, providerConfig));

        return services;
    }

    public static IServiceCollection AddLlmServicesWithCustomQuery(
        this IServiceCollection services,
        IQueryService customService)
    {
        services.AddSingleton(customService);

        return services;
    }

    public static IChatClient CreateEmptyKernel()
    {
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
            var logger = sp.GetService<ILogger<Api.Chat.PipeQueryService>>();
            return new Api.Chat.PipeQueryService(config, apiKey, logger);
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

    private static IQueryService CreateQueryService(IServiceProvider sp, ProviderConfig providerConfig)
    {
        var logger = sp.GetService<ILogger<IQueryService>>();
        var fs = sp.GetService<IFileSystem>();
        return s_factory.Create(providerConfig, logger: logger, fileSystem: fs);
    }
}
