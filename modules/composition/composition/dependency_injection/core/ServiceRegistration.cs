
namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 注册工作流服务：聚合所有子系统的 DI 注册（核心、文件操作、工具、基础设施、MCP、Bridge、Agent、调度、上下文压缩、Brain、Vault、CodeIndex、新服务、Reasoning）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="config">工作流配置根对象。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services, WorkflowConfig config) {
        services.AddSingleton(config);
        services.AddSingleton(Options.Create(config));
        services.AddSingleton(config.Provider);

        services.AddSingleton(Options.Create(new Configuration.QueryEngineConfig()));

        services.AddSingleton(McpToolDispatch.GeneratedToolHandlerRegistration_JoinCode_Composition.SafeToolNames);

        services.AddJoinCodeCompositionAutoRegisteredServices();
        services.AddJoinCodeCompositionAutoRegisteredOptions();

        services.AddGuardServices();
        services.AddCoreServices();
        services.AddFileOperationServices();
        services.AddToolServices();
        services.AddInfrastructureServices();
        services.AddMcpServices();
        // IMcpSkillProvider, ISkillDiscoveryService, SkillDiscoveryOptions, SkillOptions,
        // ISkillService, ISkillMiddleware, ICodeService, IPermissionHookExecutor,
        // IPermissionLogger, IPluginSkillBridge — [Register] 自动注册
        // Code 中间件管道 — [RegisterMiddleware] + 生成器自动注册
        // WebFetchCache, BinaryContentStorage, DomainBlocklistChecker, Web 中间件 — [Register] + [RegisterMiddleware] 自动注册
        services.AddBridgeServices();
        services.AddAgentServices();
        services.AddSchedulingServices();
        services.AddContextCompressionServices();
        services.AddBrainPipelines();
        // IInteractiveService, IPlanModeManager, IPlanService — [Register] 自动注册
        // ICacheService, IUserInteractionService — [Register] 自动注册
        services.AddVaultServices();
        services.AddCodeIndexServices(Environment.CurrentDirectory);
        services.AddNewServices();
        services.AddReasoning();

        services.AddLogging();

        return services;
    }

    /// <summary>
    /// 注册 AI 工作流服务：在 <see cref="AddWorkflowServices"/> 基础上追加 Kernel + 插件 + Release 模式 Agent 服务。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="config">工作流配置根对象。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddAiWorkflowServices(this IServiceCollection services, WorkflowConfig config) {
        services.AddWorkflowServices(config);

        services.AddKernelWithPlugins(config);

        // IQueryEngine — [Register] 自动注册（QueryEngine），无需手动 RegisterAiServices
        services.AddReleaseModeAgentServices();

        return services;
    }
}