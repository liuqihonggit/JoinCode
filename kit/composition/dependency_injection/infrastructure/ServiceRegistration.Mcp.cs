
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册 MCP 服务：MemoryCache、工具注册表、工具评分配置、超图自定义超边、
    /// Agent 工具管道（<see cref="MiddlewarePipeline{AgentToolContext}"/>）、
    /// 工具执行管道（<see cref="MiddlewarePipeline{ToolExecutionContext}"/>，含 14 个中间件）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddMcpServices(this IServiceCollection services)
    {
        services.AddMemoryCache();

        // LocalToolRegistry, RemoteClientManager, ToolCacheManager, McpToolSyncBridge,
        // PermissionAwareToolExecutor, IMcpToolRegistry (ToolRegistryAdapter),
        // IElicitationHandler (InteractiveElicitationHandler) — [Register] 自动注册
        // 直接调用本程序集生成的 AddMcpToolDispatchSingletons()（包含所有组件的 ToolHandler），
        // 而非 McpToolDispatch 程序集的 AddMcpToolDispatch()（仅含 McpToolDispatch 项目的 7 个 Handler）。
        GeneratedToolHandlerRegistration_JoinCode_Composition.AddMcpToolDispatchSingletons(services);

        // 注册 Composition 级别的工具注册委托，让 McpService 使用包含所有组件 Handler 的注册方法
        services.AddSingleton<Func<IMcpToolRegistry, IServiceProvider, CancellationToken, Task<IMcpToolRegistry>>>(
            (registry, sp, ct) => GeneratedToolHandlerRegistration_JoinCode_Composition.RegisterAllMcpToolDispatchAsync(registry, sp, ct));

        // 工具评分配置 — 从 WorkflowConfig.ToolExecution 提取
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.ToolExecution.ToolScore.ToToolScoreConfig();
        });
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return new HashSet<string>(config.ToolExecution.BlacklistedTools, StringComparer.OrdinalIgnoreCase);
        });
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return new Dictionary<string, int>(config.ToolExecution.ToolPenalties, StringComparer.OrdinalIgnoreCase);
        });

        // 超图自定义超边 — 启动时从配置加载
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.ToolExecution.CustomHyperedges;
        });

        services.AddSingleton<MiddlewarePipeline<AgentToolContext>>(sp =>
        {
            var middlewares = sp.GetServices<IAgentToolMiddleware>().Cast<IMiddleware<AgentToolContext>>();
            var builder = new PipelineBuilder<AgentToolContext>()
                .WithLoggingScope(sp.GetRequiredService<ILoggerFactory>())
                .UseRange(middlewares);
            var logger = sp.GetService<ILogger<AgentToolHandlers>>();
            if (logger is not null)
                builder.OnError((ctx, ex) => logger.LogError(ex, "[AgentPipeline] 中间件异常继续执行"));
            return builder.Build();
        });

        services.AddSingleton<MiddlewarePipeline<ToolExecutionContext>>(sp =>
        {
            var lf = sp.GetRequiredService<ILoggerFactory>();
            return new PipelineBuilder<ToolExecutionContext>()
                .WithLoggingScope(lf)
                .Use(sp.GetRequiredService<McpToolRegistry.CrashSnapshotMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.ArgumentRepairMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.RequiredParamsMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.SchemaValidationMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.AgentRestrictionMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.PermissionCheckMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.PreToolUseHookMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.RemotePolicyMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.FeatureFlagMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.OnErrorToolInjectionMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.ToolHealthScoringMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.ToolFixHookMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.PostToolUseHookMiddleware>())
                .Use(sp.GetRequiredService<McpToolRegistry.ToolExecutionMiddleware>())
                .WithShortCircuit(ctx => ctx.IsShortCircuited)
                .Build();
        });

        // IToolExecutionGateway — [Register(typeof(IToolExecutionGateway), ServiceLifetime.Singleton)] 自动注册（PermissionAwareToolExecutor）
        // IToolCategoryProvider — [Register] 自动注册（GeneratedToolCategoryProvider）
        // PromptConfig — [Register] 自动注册（DI 构造函数接收 IToolCategoryProvider）

        return services;
    }

    /// <summary>
    /// 连接 MCP 工具同步桥：订阅 <see cref="RemoteClientManager"/> 的 ToolsListChanged/ResourcesListChanged/PromptsListChanged 事件，
    /// 转发到 <see cref="McpToolSyncBridge"/> 同步到 ChatContextManager。
    /// </summary>
    /// <param name="serviceProvider">已构建的 DI 服务提供者。</param>
    public static void WireMcpToolSyncBridge(this IServiceProvider serviceProvider)
    {
        var remoteClientManager = serviceProvider.GetRequiredService<RemoteClientManager>();
        var bridge = serviceProvider.GetRequiredService<McpToolSyncBridge>();

        remoteClientManager.ToolsListChanged += async (_, _) =>
        {
            await bridge.OnToolsListChangedAsync().ConfigureAwait(false);
        };

        remoteClientManager.ResourcesListChanged += async (_, args) =>
        {
            await bridge.OnResourcesListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };

        remoteClientManager.PromptsListChanged += async (_, args) =>
        {
            await bridge.OnPromptsListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };
    }
}
