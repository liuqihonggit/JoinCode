namespace JoinCode.Llm.DependencyInjection;


public static partial class ServiceRegistration
{
    internal static readonly QueryServiceFactory s_factory = new();

    private static int s_providersRegistered;

    /// <summary>
    /// 确保默认供应商已注册到 s_factory — 幂等,DI 注册阶段和 LlmProvidersPlugin 加载时均可调用
    /// <para>解决 DI 解析 IQueryService 时插件尚未加载的时序问题(ADR 0098)</para>
    /// </summary>
    internal static void EnsureDefaultProvidersRegistered()
    {
        if (Interlocked.CompareExchange(ref s_providersRegistered, 1, 0) != 0) return;
        s_factory.RegisterProvider(ProtocolKind.Anthropic,
            (config, http, logger, fs, executor) => new AnthropicQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.Azure,
            (config, http, logger, fs, executor) => new AzureQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.Agnes,
            (config, http, logger, fs, executor) => new AgnesQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.OpenAiResponses,
            (config, http, logger, fs, executor) => new ResponsesQueryService(config, http, logger, fs, executor));
        s_factory.RegisterDefault(
            (config, http, logger, fs, executor) => new OpenAIQueryService(config, http, logger, fs, executor));
    }

    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        ProviderConfig providerConfig)
    {
        EnsureDefaultProvidersRegistered();
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
            var inner = new Api.Chat.PipeQueryService(config, apiKey, logger);
            return WrapWithFallback(inner, sp);
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
        EnsureDefaultProvidersRegistered();
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
        var resilientExecutor = CreateLlmResilientExecutor(sp);
        var inner = s_factory.Create(providerConfig, logger: logger, fileSystem: fs, resilientExecutor: resilientExecutor);
        return WrapWithFallback(inner, sp);
    }

    /// <summary>
    /// 创建 LLM API 韧性执行器 — 超时+重试+熔断，受 JCC_RESILIENCE_ENABLED 控制
    /// </summary>
    private static ResilientHttpExecutor? CreateLlmResilientExecutor(IServiceProvider sp)
    {
        var resilienceEnabled = Environment.GetEnvironmentVariable("JCC_RESILIENCE_ENABLED") is not "0";
        if (!resilienceEnabled) return null;

        var logger = sp.GetService<ILogger<ResilientHttpExecutor>>();
        var networkService = sp.GetService<INetworkConnectivityService>();
        var retryOptions = sp.GetService<IOptions<NetworkRetryOptions>>()?.Value;

        // 用统一配置创建 24h 预算驱动 policy；无配置则 fallback 到 LlmDefault
        var policy = retryOptions is not null
            ? new ResiliencePolicy
            {
                Name = "LLM",
                OperationTimeout = TimeSpan.FromSeconds(30),
                Retry = retryOptions.ToRetryConfig(),
            }
            : ResiliencePolicy.LlmDefault("LLM");

        return new ResilientHttpExecutor(policy, logger, networkService);
    }

    /// <summary>
    /// 用 StreamingFallbackDecorator + BufferedStreamingDecorator 包装内部 QueryService
    /// 对齐 TS: queryModelWithStreaming + queryModelWithoutStreaming 双路径架构
    /// </summary>
    private static IQueryService WrapWithFallback(IQueryService inner, IServiceProvider sp)
    {
        var config = StreamingFallbackConfig.FromEnvironment();
        var logger = sp.GetService<ILogger<StreamingFallbackDecorator>>();

        var envVal = Environment.GetEnvironmentVariable("JCC_DISABLE_STREAMING_FALLBACK");
        logger?.LogInformation("[FALLBACK] 流式回退配置: 已启用={Enabled}, JCC_DISABLE_STREAMING_FALLBACK={EnvVal}", config.Enabled, envVal ?? "(未设置)");

        var withFallback = new StreamingFallbackDecorator(inner, config, logger);
        var withBufferedStreaming = new BufferedStreamingDecorator(withFallback, logger);

        return withBufferedStreaming;
    }
}
