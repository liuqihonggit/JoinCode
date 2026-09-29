namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 注册 Bridge 服务：BridgeConfig、TransportConfiguration、BridgeApiClient 工厂、
    /// V1/V2 初始化管道、HandleWork 管道、Shutdown 管道、Run 管道等。
    /// <para>大部分 Bridge 服务通过 [Register] 自动注册，本方法补充需从 WorkflowConfig 提取的配置和中间件管道组装。</para>
    /// <para>本方法为编排入口，具体注册逻辑拆分到 internal 子方法，便于确定性单元测试覆盖。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddBridgeServices(this IServiceCollection services) {
        RegisterBridgeConfigExtraction(services);
        RegisterSubAgentStallDefense(services);
        RegisterBridgeApiClientFactory(services);
        RegisterTransportConfiguration(services);
        RegisterV1BridgeInitPipeline(services);
        RegisterV2BridgeInitPipeline(services);
        RegisterHandleWorkPipeline(services);
        RegisterShutdownPipeline(services);
        RegisterRunPipeline(services);
        return services;
    }

    /// <summary>
    /// 注册 BridgeConfig/SubAgentConcurrencyOptions/SubAgentLivenessOptions 配置提取 — 从 WorkflowConfig 提取，不能 [Register]。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterBridgeConfigExtraction(IServiceCollection services) {
        services.AddSingleton<BridgeConfig>(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.Bridge;
        });

        services.AddSingleton<SubAgentConcurrencyOptions>(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.SubAgentConcurrency;
        });

        services.AddSingleton<SubAgentLivenessOptions>(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.SubAgentLiveness;
        });
    }

    /// <summary>
    /// 注册子代理卡死防护纵深防御体系 L1-L4（ADR 0106）及其 HostedService。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterSubAgentStallDefense(IServiceCollection services) {
        services.AddSubAgentStallDefense();
        services.AddHostedService<SubAgentStallDefenseHostedService>();
    }

    /// <summary>
    /// 注册 BridgeApiClient 手动工厂 — 覆盖 [Register] 自动注册。
    /// <para>原因: BridgeApiClient 有两个 public 构造函数（HttpClient 版和 BridgeConfig 版），DI 容器无法选择导致歧义；工厂方法明确使用 BridgeConfig 版构造函数。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterBridgeApiClientFactory(IServiceCollection services) {
        services.AddSingleton<BridgeApiClient>(sp => {
            var config = sp.GetService<BridgeConfig>();
            var options = sp.GetService<BridgeApiOptions>();
            var logger = sp.GetService<ILogger<BridgeApiClient>>();
            return new BridgeApiClient(config, options, logger);
        });
    }

    /// <summary>
    /// 注册 TransportConfiguration — 从 BridgeConfig 初始化（跨组件依赖，不能 [Register]）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterTransportConfiguration(IServiceCollection services) {
        services.AddSingleton<TransportConfiguration>(sp => {
            var config = sp.GetRequiredService<BridgeConfig>();
            return new TransportConfiguration {
                PreferredProtocol = config.Protocol,
                WebSocketEndpoint = config.WebSocketEndpoint,
                SseEndpoint = config.SseEndpoint,
                AutoReconnect = config.AutoReconnect,
                MaxReconnectAttempts = config.MaxReconnectAttempts
            };
        });
    }

    /// <summary>
    /// 注册 V1 Bridge 初始化管道 — 洋葱模型：中间件顺序即注册顺序。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterV1BridgeInitPipeline(IServiceCollection services) {
        services.AddSingleton<MiddlewarePipeline<V1BridgeInitContext>>(sp =>
            new PipelineBuilder<V1BridgeInitContext>()
                .Use(new TokenValidationMiddleware<V1BridgeInitContext>())
                .Use(sp.GetRequiredService<V1PerpetualPointerMiddleware>())
                .Use(sp.GetRequiredService<V1PerpetualSessionValidationMiddleware>())
                .Use(sp.GetRequiredService<V1EnvRegistrationMiddleware>())
                .Use(sp.GetRequiredService<V1SessionCreationMiddleware>())
                .Use(sp.GetRequiredService<V1PointerWriteMiddleware>())
                .Use(sp.GetRequiredService<V1WorkPollSetupMiddleware>())
                .OnError((ctx, ex) => {
                    ctx.Logger?.LogError(ex, "Bridge v1 init step failed");
                })
                .Build());
    }

    /// <summary>
    /// 注册 V2 Bridge 初始化管道 — 洋葱模型：中间件顺序即注册顺序。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterV2BridgeInitPipeline(IServiceCollection services) {
        services.AddSingleton<MiddlewarePipeline<V2BridgeInitContext>>(sp =>
            new PipelineBuilder<V2BridgeInitContext>()
                .Use(new TokenValidationMiddleware<V2BridgeInitContext>())
                .Use(sp.GetRequiredService<V2CodeSessionMiddleware>())
                .Use(sp.GetRequiredService<V2CredentialsMiddleware>())
                .Use(sp.GetRequiredService<V2TransportSetupMiddleware>())
                .Use(sp.GetRequiredService<V2TokenRefreshAndCallbacksMiddleware>())
                .OnError((ctx, ex) => {
                    ctx.Logger?.LogError(ex, "Bridge v2 init step failed");
                })
                .Build());
    }

    /// <summary>
    /// 注册 Bridge HandleWork 管道 — 处理 Bridge 分配的工作任务。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterHandleWorkPipeline(IServiceCollection services) {
        services.AddSingleton<MiddlewarePipeline<HandleWorkContext>>(sp =>
            new PipelineBuilder<HandleWorkContext>()
                .Use(sp.GetRequiredService<WorkAckMiddleware>())
                .Use(sp.GetRequiredService<WorkSecretDecodeMiddleware>())
                .Use(sp.GetRequiredService<WorkCapacityCheckMiddleware>())
                .Use(sp.GetRequiredService<WorkHealthcheckMiddleware>())
                .Use(sp.GetRequiredService<WorkCcrV2RegisterMiddleware>())
                .Use(sp.GetRequiredService<WorkWorktreeMiddleware>())
                .Use(sp.GetRequiredService<WorkSpawnMiddleware>())
                .Use(sp.GetRequiredService<WorkSessionTrackMiddleware>())
                .WithHooks(sp)
                .Build());
    }

    /// <summary>
    /// 注册 Bridge Shutdown 管道 — 关闭 Bridge 时清理资源。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterShutdownPipeline(IServiceCollection services) {
        services.AddSingleton<MiddlewarePipeline<ShutdownContext>>(sp =>
            new PipelineBuilder<ShutdownContext>()
                .Use(sp.GetRequiredService<ShutdownCancelLoopMiddleware>())
                .Use(sp.GetRequiredService<ShutdownDeregisterMiddleware>())
                .Use(sp.GetRequiredService<ShutdownSubprocessesMiddleware>())
                .Use(sp.GetRequiredService<ShutdownClearPointerMiddleware>())
                .Use(sp.GetRequiredService<ShutdownArchiveMiddleware>())
                .WithHooks(sp)
                .Build());
    }

    /// <summary>
    /// 注册 Bridge Run 管道 — Bridge 运行流程。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    internal static void RegisterRunPipeline(IServiceCollection services) {
        services.AddSingleton<MiddlewarePipeline<BridgeRunContext>>(sp =>
            new PipelineBuilder<BridgeRunContext>()
                .Use(sp.GetRequiredService<RunValidationMiddleware>())
                .Use(sp.GetRequiredService<RunSpawnModeMiddleware>())
                .Use(sp.GetRequiredService<RunResumeMiddleware>())
                .WithHooks(sp)
                .Build());
    }
}
