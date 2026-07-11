﻿﻿﻿using Core.Bridge.Init;
using Core.Bridge.Init.V1;
using Core.Bridge.Init.V2;
using Infrastructure.Pipeline;
using Infrastructure.Pipeline.Middlewares;

namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddBridgeServices(this IServiceCollection services)
    {
        // 以下服务已通过 [Register] 自动注册，无需手动注册：
        // BridgeJwtService — [Register]（从 BridgeConfig 构造）
        // IWorkSecretStore — [Register]（WorkSecretStore，从 BridgeConfig 构造）
        // IConnectionManager — [Register]（ConnectionManager）
        // IMessageRouter — [Register]（StringMessageRouter）
        // ITransportManager — [Register]（TransportManager）
        // BridgeSessionFactory — [Register]
        // BridgeSessionConfiguration — [Register]（从 BridgeConfig 构造）
        // BridgeSessionRunner — [Register]
        // CapacityWakeOptions — [Register]（从 BridgeConfig 构造）
        // CapacityWakeService — [Register]
        // BridgeApiOptions — [Register]（从 BridgeConfig 构造）
        // BridgeApiClient — [Register]（DI 构造函数，从 BridgeConfig 创建 HttpClient）
        // BridgeServerSecurity — [Register]（record，DI 自动解析可选参数）
        // BridgeServerSession — [Register]（record，DI 自动解析可选参数）
        // BridgeServer — [Register]
        // BridgeServerHostedService — [Register]
        // MessageHandlerContext — [Register]（record，DI 自动解析可选参数）
        // MessageHandlerCoordinator — [Register]
        // BridgeClientSession — [Register]（record，DI 自动解析可选参数）
        // BridgeClientOptions — [Register]
        // BridgeClient — [Register]
        // PeerSessionManager — [Register]
        // BridgeUIService — [Register]
        // PollConfigManager — [Register]
        // V1PerpetualPointerMiddleware — [Register]
        // V1PerpetualSessionValidationMiddleware — [Register]
        // V1EnvRegistrationMiddleware — [Register]
        // V1SessionCreationMiddleware — [Register]
        // V1PointerWriteMiddleware — [Register]
        // V1WorkPollSetupMiddleware — [Register]
        // V2CodeSessionMiddleware — [Register]
        // V2CredentialsMiddleware — [Register]
        // V2TransportSetupMiddleware — [Register]
        // V2TokenRefreshAndCallbacksMiddleware — [Register]

        // BridgeConfig 需从 WorkflowConfig.Bridge 提取，不能 [Register]
        services.AddSingleton<BridgeConfig>(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.Bridge;
        });

        // BridgeApiClient 手动工厂注册 — 覆盖 [Register] 自动注册
        // 原因: BridgeApiClient 有两个 public 构造函数（HttpClient 版和 BridgeConfig 版），DI 容器无法选择导致歧义
        // 工厂方法明确使用 BridgeConfig 版构造函数，避免歧义
        services.AddSingleton<BridgeApiClient>(sp =>
        {
            var config = sp.GetService<BridgeConfig>();
            var options = sp.GetService<BridgeApiOptions>();
            var logger = sp.GetService<ILogger<BridgeApiClient>>();
            return new BridgeApiClient(config, options, logger);
        });

        // TransportConfiguration 需从 BridgeConfig 初始化，不能 [Register]（跨组件依赖）
        services.AddSingleton<TransportConfiguration>(sp => {
            var config = sp.GetRequiredService<BridgeConfig>();
            return new TransportConfiguration
            {
                PreferredProtocol = config.Protocol,
                WebSocketEndpoint = config.WebSocketEndpoint,
                SseEndpoint = config.SseEndpoint,
                AutoReconnect = config.AutoReconnect,
                MaxReconnectAttempts = config.MaxReconnectAttempts
            };
        });

        // V1 Bridge 初始化管道 — 洋葱模型：中间件顺序即注册顺序
        services.AddSingleton<MiddlewarePipeline<V1BridgeInitContext>>(sp =>
            new PipelineBuilder<V1BridgeInitContext>()
                .Use(new TokenValidationMiddleware<V1BridgeInitContext>())
                .Use(sp.GetRequiredService<V1PerpetualPointerMiddleware>())
                .Use(sp.GetRequiredService<V1PerpetualSessionValidationMiddleware>())
                .Use(sp.GetRequiredService<V1EnvRegistrationMiddleware>())
                .Use(sp.GetRequiredService<V1SessionCreationMiddleware>())
                .Use(sp.GetRequiredService<V1PointerWriteMiddleware>())
                .Use(sp.GetRequiredService<V1WorkPollSetupMiddleware>())
                .OnError((ctx, ex) =>
                {
                    ctx.Logger?.LogError(ex, "Bridge v1 init step failed");
                })
                .Build());

        // V2 Bridge 初始化管道 — 洋葱模型：中间件顺序即注册顺序
        services.AddSingleton<MiddlewarePipeline<V2BridgeInitContext>>(sp =>
            new PipelineBuilder<V2BridgeInitContext>()
                .Use(new TokenValidationMiddleware<V2BridgeInitContext>())
                .Use(sp.GetRequiredService<V2CodeSessionMiddleware>())
                .Use(sp.GetRequiredService<V2CredentialsMiddleware>())
                .Use(sp.GetRequiredService<V2TransportSetupMiddleware>())
                .Use(sp.GetRequiredService<V2TokenRefreshAndCallbacksMiddleware>())
                .OnError((ctx, ex) =>
                {
                    ctx.Logger?.LogError(ex, "Bridge v2 init step failed");
                })
                .Build());

        // Bridge HandleWork 管道 — 处理 Bridge 分配的工作任务
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

        // Bridge Shutdown 管道 — 关闭 Bridge 时清理资源
        services.AddSingleton<MiddlewarePipeline<ShutdownContext>>(sp =>
            new PipelineBuilder<ShutdownContext>()
                .Use(sp.GetRequiredService<ShutdownCancelLoopMiddleware>())
                .Use(sp.GetRequiredService<ShutdownDeregisterMiddleware>())
                .Use(sp.GetRequiredService<ShutdownSubprocessesMiddleware>())
                .Use(sp.GetRequiredService<ShutdownClearPointerMiddleware>())
                .Use(sp.GetRequiredService<ShutdownArchiveMiddleware>())
                .WithHooks(sp)
                .Build());

        // Bridge Run 管道 — Bridge 运行流程
        services.AddSingleton<MiddlewarePipeline<BridgeRunContext>>(sp =>
            new PipelineBuilder<BridgeRunContext>()
                .Use(sp.GetRequiredService<RunValidationMiddleware>())
                .Use(sp.GetRequiredService<RunSpawnModeMiddleware>())
                .Use(sp.GetRequiredService<RunResumeMiddleware>())
                .WithHooks(sp)
                .Build());

        return services;
    }
}
