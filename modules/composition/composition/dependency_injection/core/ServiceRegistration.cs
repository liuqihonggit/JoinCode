
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
        // ADR 0160 — 只读命令探测委托：委托 guard 的 IReadOnlyCommandDetector，供 QueryEngine 分类器判定 Bash/PowerShell 并行安全
        services.TryAddSingleton<ReadOnlyCommandProbe>(sp => {
            var detector = sp.GetService<IReadOnlyCommandDetector>();
            return command => detector is not null
                && detector.CheckReadOnlyConstraints(command).Behavior == PermissionBehavior.Allow;
        });

        var sw = PerfTimer.Start("    AddJoinCodeCompositionAutoRegisteredServices");
        var countBefore = services.Count;
        services.AddJoinCodeCompositionAutoRegisteredServices();
        sw?.Stop();
        PerfTimer.Mark($"    AddJoinCodeCompositionAutoRegisteredServices: {services.Count - countBefore} services (before={countBefore}, after={services.Count})");
        sw = PerfTimer.Start("    AddJoinCodeCompositionAutoRegisteredOptions");
        services.AddJoinCodeCompositionAutoRegisteredOptions();
        sw?.Stop();

        sw = PerfTimer.Start("    AddGuardServices");
        services.AddGuardServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddCoreServices");
        services.AddCoreServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddFileOperationServices");
        services.AddFileOperationServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddToolServices");
        services.AddToolServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddInfrastructureServices");
        services.AddInfrastructureServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddMcpServices");
        services.AddMcpServices();
        sw?.Stop();
        // IMcpSkillProvider, ISkillDiscoveryService, SkillDiscoveryOptions, SkillOptions,
        // ISkillService, ISkillMiddleware, ICodeService, IPermissionHookExecutor,
        // IPermissionLogger, IPluginSkillBridge — [Register] 自动注册
        // Code 中间件管道 — [RegisterMiddleware] + 生成器自动注册
        // WebFetchCache, BinaryContentStorage, DomainBlocklistChecker, Web 中间件 — [Register] + [RegisterMiddleware] 自动注册
        sw = PerfTimer.Start("    AddBridgeServices");
        services.AddBridgeServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddAgentServices");
        services.AddAgentServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddSchedulingServices");
        services.AddSchedulingServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddContextCompressionServices");
        services.AddContextCompressionServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddBrainPipelines");
        services.AddBrainPipelines();
        sw?.Stop();
        // IInteractiveService, IPlanModeManager, IPlanService — [Register] 自动注册
        // ICacheService, IUserInteractionService — [Register] 自动注册
        sw = PerfTimer.Start("    AddVaultServices");
        services.AddVaultServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddCodeIndexServices");
        services.AddCodeIndexServices(Environment.CurrentDirectory);
        sw?.Stop();
        sw = PerfTimer.Start("    AddNewServices");
        services.AddNewServices();
        sw?.Stop();
        sw = PerfTimer.Start("    AddReasoning");
        services.AddReasoning();
        sw?.Stop();

        // Node Plugin Architecture (DSG048): 注册 NodeRegistry + NodeProfileResolver
        sw = PerfTimer.Start("    AddNodeRegistry");
        services.AddSingleton<NodeProfileResolver>(sp => {
            var profiles = NodeProfileLoader.LoadFromResources(fileSystem: sp.GetService<IFileSystem>());
            return new NodeProfileResolver(profiles);
        });
        services.AddSingleton<INodeRegistry>(sp => NodeRegistry.Build(sp));
        sw?.Stop();

        // TASK100 S3：把 profile 的 llm-providers 声明注入 LLM 层（启用集合）。
        // 装配层负责读 profile；LLM 层只认「启用集合」这一个纯数据契约，不需要认识 IProfileManager。
        sw = PerfTimer.Start("    AddLlmProviderDeclaration");
        services.AddSingleton(sp => {
            var profiles = sp.GetService<JoinCode.Abstractions.Composition.IProfileManager>();
            if (profiles is null)
                return Api.LLM.QueryServices.Nodes.LlmProviderDeclaration.AllEnabled;

            var consumerId = Api.LLM.QueryServices.Nodes.LlmProviderNodes.ConsumerId;
            if (!profiles.Current.Consumers.TryGetValue(consumerId, out var consumer) || consumer.Nodes.Count == 0)
                return Api.LLM.QueryServices.Nodes.LlmProviderDeclaration.AllEnabled;

            var declaredIds = consumer.Nodes.Select(static node => node.Id).ToArray();
            Diag.WriteLine($"[LLM] provider declaration from profile '{consumerId}': {string.Join(", ", declaredIds)}");
            return Api.LLM.QueryServices.Nodes.LlmProviderDeclaration.FromDeclaredIds(declaredIds);
        });
        sw?.Stop();

        // TASK100 S3：角色启用集合同样由装配层读 profile 后注入（role 契约，同 role+variant 互斥）。
        sw = PerfTimer.Start("    AddAgentRoleDeclaration");
        services.AddSingleton(sp => {
            var profiles = sp.GetService<JoinCode.Abstractions.Composition.IProfileManager>();
            if (profiles is null)
                return JoinCode.Abstractions.Models.Agent.AgentRoleDeclaration.AllEnabled;

            var consumerId = JoinCode.Abstractions.Models.Agent.AgentRoleDeclaration.ConsumerId;
            if (!profiles.Current.Consumers.TryGetValue(consumerId, out var consumer) || consumer.Nodes.Count == 0)
                return JoinCode.Abstractions.Models.Agent.AgentRoleDeclaration.AllEnabled;

            var declaredIds = consumer.Nodes.Select(static node => node.Id).ToArray();
            Diag.WriteLine($"[ROLES] declaration from profile '{consumerId}': {string.Join(", ", declaredIds)}");
            return JoinCode.Abstractions.Models.Agent.AgentRoleDeclaration.FromDeclaredIds(declaredIds);
        });
        sw?.Stop();

        sw = PerfTimer.Start("    AddAotHandlerRegistry");
        AotHandlerRegistrations_JoinCode_Composition.AddAotHandlerRegistry(services);
        sw?.Stop();

        sw = PerfTimer.Start("    AddRegistryHub");
        services.AddSingleton<IFileReadListenerRegistry, FileReadListenerRegistry>();
        services.AddSingleton<IRegistryHub, RegistryHub>();
        sw?.Stop();

        sw = PerfTimer.Start("    AddDynamicPluginServices");
        services.AddDynamicPluginServices();
        sw?.Stop();

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

        var sw = PerfTimer.Start("    AddKernelWithPlugins");
        services.AddKernelWithPlugins(config);
        sw?.Stop();

        // IQueryEngine — [Register] 自动注册（QueryEngine），无需手动 RegisterAiServices
        sw = PerfTimer.Start("    AddReleaseModeAgentServices");
        services.AddReleaseModeAgentServices();
        sw?.Stop();

        return services;
    }
}