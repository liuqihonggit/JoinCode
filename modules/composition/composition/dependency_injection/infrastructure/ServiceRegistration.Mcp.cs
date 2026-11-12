
namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 注册 MCP 服务：MemoryCache、工具注册表、工具评分配置、超图自定义超边、
    /// Agent 工具管道（<see cref="MiddlewarePipeline{AgentToolContext}"/>）、
    /// 工具执行管道（<see cref="MiddlewarePipeline{ToolExecutionContext}"/>，含 15 个中间件）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddMcpServices(this IServiceCollection services) {
        services.AddMemoryCache();

        // LocalToolRegistry, RemoteClientManager, ToolCacheManager, McpToolSyncBridge,
        // PermissionAwareToolExecutor, IMcpToolRegistry (ToolRegistryAdapter),
        // IElicitationHandler (InteractiveElicitationHandler) — [Register] 自动注册
        // 直接调用本程序集生成的 AddMcpToolDispatchSingletons()（包含所有组件的 ToolHandler），
        // 而非 McpToolDispatch 程序集的 AddMcpToolDispatch()（仅含 McpToolDispatch 项目的 7 个 Handler）。
        GeneratedToolHandlerRegistration_JoinCode_Composition.AddMcpToolDispatchSingletons(services);

        // 注册 Composition 级别的工具注册委托，让 McpService 使用包含所有组件 Handler 的注册方法
        // ToolNameFilter 委托由 EngineApplicationBuilder.BuildHost 根据 SkipChatServices 注册
        services.AddSingleton<Func<IMcpToolRegistry, IServiceProvider, CancellationToken, Task<IMcpToolRegistry>>>(
            async (registry, sp, ct) => {
                var toolFilter = sp.GetService<Func<string, bool>>();
                var sw = PerfTimer.Start($"  RegisterAll (filter={toolFilter is not null})");
                var result = await GeneratedToolHandlerRegistration_JoinCode_Composition.RegisterAllMcpToolDispatchAsync(registry, sp, ct, toolFilter).ConfigureAwait(false);
                sw?.Stop();
                return result;
            });

        // 工具评分配置 — 从 WorkflowConfig.ToolExecution 提取
        services.AddSingleton(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.ToolExecution.ToolScore.ToToolScoreConfig();
        });
        services.AddSingleton(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return new HashSet<string>(config.ToolExecution.BlacklistedTools, StringComparer.OrdinalIgnoreCase);
        });
        services.AddSingleton(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return new Dictionary<string, int>(config.ToolExecution.ToolPenalties, StringComparer.OrdinalIgnoreCase);
        });

        // 超图自定义超边 — 启动时从配置加载
        services.AddSingleton(sp => {
            var config = sp.GetRequiredService<WorkflowConfig>();
            return config.ToolExecution.CustomHyperedges;
        });

        // MiddlewarePipeline<AgentToolContext> — 数据驱动组装（AddDataDrivenPipelines 注册）
        // MiddlewarePipeline<ToolExecutionContext> — 数据驱动组装（AddDataDrivenPipelines 注册）

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
    public static void WireMcpToolSyncBridge(this IServiceProvider serviceProvider) {
        var remoteClientManager = serviceProvider.GetRequiredService<RemoteClientManager>();
        var bridge = serviceProvider.GetRequiredService<McpToolSyncBridge>();

        remoteClientManager.ToolsListChanged += async (_, _) => {
            await bridge.OnToolsListChangedAsync().ConfigureAwait(false);
        };

        remoteClientManager.ResourcesListChanged += async (_, args) => {
            await bridge.OnResourcesListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };

        remoteClientManager.PromptsListChanged += async (_, args) => {
            await bridge.OnPromptsListChangedAsync(args.ClientId, args.SyncResult).ConfigureAwait(false);
        };
    }
}