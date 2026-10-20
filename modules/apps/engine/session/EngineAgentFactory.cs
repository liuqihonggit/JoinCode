namespace JoinCode.Cli;

/// <summary>
/// 引擎主代理工厂 — 多宿主共享的主代理构造入口（ADR 0143 引擎库）。
/// 从 CliSession.CreateMainAgent 提取，供 CLI 宿主（CliSession/BridgeServeCommand）与
/// 未来 GUI/TUI 宿主复用同一构造链路（统一 Spawn 管道），禁止散落重复。
/// </summary>
public sealed class EngineAgentFactory {
    private EngineAgentFactory() { }

    /// <summary>
    /// 创建主代理 — 通过统一 Spawn 管道创建，IsMainAgent=true。
    /// 管道各中间件对主代理 no-op（Definition/Prompt/ContextSetup/LifecycleSpawn 等），仅 RecordContext/PermissionRouting/Metadata 生效。
    /// 返回 null 表示 DI 中无 IQueryEngine，SessionController 回退到直接调用 ChatService。
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供者；null 时无引擎上下文直接返回 null</param>
    /// <param name="sessionObjectId">会话 ObjectId — 全局唯一标识，跨域引用用此</param>
    public static AgentBase? CreateMainAgent(IServiceProvider? serviceProvider, ObjectId sessionObjectId) {
        if (serviceProvider is null) return null;
        var queryEngine = serviceProvider.GetService<IQueryEngine>();
        if (queryEngine is null) return null;

        var contextManager = serviceProvider.GetService<IChatContextManager>();

        var mainAgent = new AgentBase(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: null,
            name: "mainAgent",
            role: AgentRole.Coordinator,
            sessionId: sessionObjectId,
            contextManager: contextManager);

        var spawnPipeline = serviceProvider.GetService<MiddlewarePipeline<UnifiedSpawnContext>>();
        if (spawnPipeline is not null) {
            try {
                var context = new UnifiedSpawnContext {
                    Task = string.Empty,
                    IsMainAgent = true,
                    Agent = mainAgent,
                };
                spawnPipeline.ExecuteAsync(context, default).GetAwaiter().GetResult();
            } catch (Exception ex) {
                serviceProvider.GetService<ILogger<EngineAgentFactory>>()?.LogWarning(ex, "[EngineAgentFactory] 主代理走统一管道失败，回退到直接创建");
            }
        }

        return mainAgent;
    }
}
