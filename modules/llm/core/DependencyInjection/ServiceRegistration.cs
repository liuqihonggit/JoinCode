namespace JoinCode.Llm.DependencyInjection;


public static partial class ServiceRegistration {
    internal static readonly QueryServiceFactory s_factory = new();

    private static int s_providersRegistered;

    /// <summary>
    /// 确保默认供应商已注册到 s_factory — 幂等,DI 注册阶段和 LlmProvidersPlugin 加载时均可调用
    /// <para>解决 DI 解析 IQueryService 时插件尚未加载的时序问题(ADR 0098)</para>
    /// </summary>
    internal static void EnsureDefaultProvidersRegistered() {
        if (Interlocked.CompareExchange(ref s_providersRegistered, 1, 0) != 0) return;
        s_factory.RegisterProvider(ProtocolKind.Anthropic,
            (config, http, logger, fs, executor) => new AnthropicQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.Azure,
            (config, http, logger, fs, executor) => new AzureQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.Agnes,
            (config, http, logger, fs, executor) => new AgnesQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.OpenAiResponses,
            (config, http, logger, fs, executor) => new ResponsesQueryService(config, http, logger, fs, executor));
        s_factory.RegisterProvider(ProtocolKind.Jev,
            (config, http, logger, fs, executor) => new JevQueryService(config, http, logger, fs, executor));
        s_factory.RegisterDefault(
            (config, http, logger, fs, executor) => new OpenAIQueryService(config, http, logger, fs, executor));
    }

    /// <summary>添加 LLM 服务到 DI 容器。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="providerConfig">供应商配置。</param>
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        ProviderConfig providerConfig) {
        EnsureDefaultProvidersRegistered();
        services.AddSingleton<IQueryService>(sp => CreateQueryService(sp, providerConfig));

        return services;
    }

    /// <summary>添加 LLM 服务到 DI 容器（使用自定义查询服务）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="customService">自定义查询服务实例。</param>
    public static IServiceCollection AddLlmServicesWithCustomQuery(
        this IServiceCollection services,
        IQueryService customService) {
        services.AddSingleton(customService);

        return services;
    }

    /// <summary>创建空内核（使用空查询服务）。</summary>
    public static IChatClient CreateEmptyKernel() {
        return new ChatClient(new EmptyQueryService());
    }

    /// <summary>
    /// 注册 Pipe 查询服务（通过命名管道与外部 LLM 服务通信）
    /// </summary>
    public static IServiceCollection AddPipeQueryService(
        this IServiceCollection services,
        PipeTransportConfig config,
        string? apiKey = null) {
        services.AddSingleton(config);
        services.AddSingleton<IQueryService>(sp => {
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
        PipeTransportConfig? pipeEndpoint = null) {
        EnsureDefaultProvidersRegistered();
        if (pipeEndpoint != null) {
            services.AddPipeQueryService(pipeEndpoint, providerConfig.ApiKey);
        } else {
            services.AddSingleton<IQueryService>(sp => CreateQueryService(sp, providerConfig));
        }

        return services;
    }

    /// <summary>
    /// 注册 Kernel 及其动态插件
    /// </summary>
    public static IServiceCollection AddKernelWithDynamicPlugins(
        this IServiceCollection services,
        ProviderConfig providerConfig) {
        return services;
    }

    private static IQueryService CreateQueryService(IServiceProvider sp, ProviderConfig providerConfig) {
        var logger = sp.GetService<ILogger<IQueryService>>();
        var fs = sp.GetService<IFileSystem>();
        var resilientExecutor = CreateLlmResilientExecutor(sp);
        var inner = s_factory.Create(providerConfig, logger: logger, fileSystem: fs, resilientExecutor: resilientExecutor);
        return WrapWithFallback(inner, sp);
    }

    /// <summary>
    /// 创建 LLM API 韧性执行器 — 超时+重试+熔断，受 JCC_RESILIENCE_ENABLED 控制
    /// <para>无人值守模式（CLAUDE_CODE_UNATTENDED_RETRY=1）：启用 6h 单次 cap + 30s 心跳 + 仅 429/529 无限重试</para>
    /// </summary>
    private static ResilientHttpExecutor? CreateLlmResilientExecutor(IServiceProvider sp) {
        var resilienceEnabled = Environment.GetEnvironmentVariable("JCC_RESILIENCE_ENABLED") is not "0";
        if (!resilienceEnabled) return null;

        var logger = sp.GetService<ILogger<ResilientHttpExecutor>>();
        var networkService = sp.GetService<INetworkConnectivityService>();
        var retryOptions = sp.GetService<IOptions<NetworkRetryOptions>>()?.Value;
        var isUnattended = IsUnattendedRetryEnabled();

        var retryConfig = retryOptions is not null
            ? retryOptions.ToRetryConfig(isUnattended)
            : new RetryConfig();

        // JCC_DISABLE_RETRY=true 时禁用 ResilientHttpExecutor 层重试（QueryEngine.IsRetryable 已检查此开关，
        // 但 ResilientHttpExecutor 有独立重试循环，需在此设置 ShouldRetry 委托才能生效）
        if (Environment.GetEnvironmentVariable("JCC_DISABLE_RETRY") is { } disableRetryVal
            && (disableRetryVal.Equals("true", StringComparison.OrdinalIgnoreCase) || disableRetryVal == "1")) {
            retryConfig = new RetryConfig {
                MaxRetries = 0,
                BaseDelay = retryConfig.BaseDelay,
                MaxDelay = retryConfig.MaxDelay,
                Strategy = retryConfig.Strategy,
                ShouldRetry = _ => false,
                TotalBudget = retryConfig.TotalBudget,
                PauseBudgetOnNetworkUnavailable = retryConfig.PauseBudgetOnNetworkUnavailable,
                RetryAfterExtractor = retryConfig.RetryAfterExtractor,
                SingleDelayCap = retryConfig.SingleDelayCap,
                HeartbeatInterval = retryConfig.HeartbeatInterval,
                HeartbeatCallback = retryConfig.HeartbeatCallback,
                TransientErrorPredicate = retryConfig.TransientErrorPredicate,
            };
        }

        if (isUnattended) {
            retryConfig = new RetryConfig {
                MaxRetries = retryConfig.MaxRetries,
                BaseDelay = retryConfig.BaseDelay,
                MaxDelay = retryConfig.MaxDelay,
                Strategy = retryConfig.Strategy,
                ShouldRetry = retryConfig.ShouldRetry,
                TotalBudget = retryConfig.TotalBudget,
                PauseBudgetOnNetworkUnavailable = retryConfig.PauseBudgetOnNetworkUnavailable,
                RetryAfterExtractor = retryConfig.RetryAfterExtractor,
                SingleDelayCap = retryConfig.SingleDelayCap,
                HeartbeatInterval = retryConfig.HeartbeatInterval,
                HeartbeatCallback = (ex, remaining, attempt) =>
                    logger?.LogWarning("[LLM:UNATTENDED] 重试等待 {RemainingMs:F0}ms (尝试 {Attempt}) | {ExType}: {Message}",
                        remaining.TotalMilliseconds, attempt, ex.GetType().Name, ex.Message),
                TransientErrorPredicate = IsTransientCapacityError,
            };
        }

        var policy = retryOptions is not null
            ? new ResiliencePolicy {
                Name = "LLM",
                OperationTimeout = TimeSpan.FromSeconds(30),
                Retry = retryConfig,
            }
            : ResiliencePolicy.LlmDefault("LLM");

        return new ResilientHttpExecutor(policy, logger, networkService);
    }

    /// <summary>
    /// 无人值守重试是否启用 — 对齐基准 withRetry.ts:100-104 isPersistentRetryEnabled
    /// <para>环境变量 CLAUDE_CODE_UNATTENDED_RETRY=1/true 启用</para>
    /// </summary>
    private static bool IsUnattendedRetryEnabled() =>
        Environment.GetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY") is { } val
        && (val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1");

    /// <summary>
    /// 判断是否为容量错误（429/529）— 对齐基准 withRetry.ts:106-110 isTransientCapacityError
    /// </summary>
    private static bool IsTransientCapacityError(Exception ex) =>
        ex is ApiException api && (api.StatusCode == 429 || api.StatusCode == 529);

    /// <summary>
    /// 用 StreamingFallbackDecorator + BufferedStreamingDecorator 包装内部 QueryService
    /// 对齐 TS: queryModelWithStreaming + queryModelWithoutStreaming 双路径架构
    /// </summary>
    private static IQueryService WrapWithFallback(IQueryService inner, IServiceProvider sp) {
        var config = StreamingFallbackConfig.FromEnvironment();
        var logger = sp.GetService<ILogger<StreamingFallbackDecorator>>();

        var envVal = Environment.GetEnvironmentVariable("JCC_DISABLE_STREAMING_FALLBACK");
        logger?.LogInformation("[FALLBACK] 流式回退配置: 已启用={Enabled}, JCC_DISABLE_STREAMING_FALLBACK={EnvVal}", config.Enabled, envVal ?? "(未设置)");

        var withFallback = new StreamingFallbackDecorator(inner, config, logger);
        var withBufferedStreaming = new BufferedStreamingDecorator(withFallback, logger);

        return withBufferedStreaming;
    }
}