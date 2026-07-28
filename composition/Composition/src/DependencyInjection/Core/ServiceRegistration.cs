
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddWorkflowServices(this IServiceCollection services, WorkflowConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(Options.Create(config));
        // ProviderConfig — 从 WorkflowConfig.Provider 提取，供 QueryService DI 构造函数自动解析
        services.AddSingleton(config.Provider);

        // 注册 QueryEngineConfig 为 IOptions — 供 ContentReplacementService 等自动解析
        services.AddSingleton(Options.Create(new Configuration.QueryEngineConfig()));

        // 源码生成器按程序集生成独立方法名，避免库与 Exe 之间的 CS0121 歧义
        // Composition 程序集名 "JoinCode.Composition" → 清理为 "JoinCodeComposition"
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

    public static IServiceCollection AddAiWorkflowServices(this IServiceCollection services, WorkflowConfig config)
    {
        services.AddWorkflowServices(config);

        services.AddKernelWithPlugins(config);

        // IQueryEngine — [Register] 自动注册（QueryEngine），无需手动 RegisterAiServices
        services.AddReleaseModeAgentServices();

        return services;
    }
}
