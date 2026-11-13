namespace Core.DependencyInjection;

/// <summary>
/// 注册中心聚合门面实现 — 统一访问通用 Registry，减少调用方依赖散落
/// </summary>
public sealed class RegistryHub : IRegistryHub {
    /// <summary>工具注册表</summary>
    public IToolRegistry Tools { get; }

    /// <summary>目标调度注册表</summary>
    public IGoalRegistry Goals { get; }

    /// <summary>系统执行器注册表</summary>
    public ISystemActuatorRegistry SystemActuators { get; }

    /// <summary>Agent 角色注册表</summary>
    public IAgentRoleRegistry AgentRoles { get; }

    /// <summary>代码索引器注册表</summary>
    public ICodeIndexerRegistry CodeIndexers { get; }

    /// <summary>前台任务注册表</summary>
    public IForegroundTaskRegistry ForegroundTasks { get; }

    /// <summary>文件读取监听器注册表</summary>
    public IFileReadListenerRegistry FileReadListeners { get; }

    /// <summary>命令注册表</summary>
    public ICommandRegistry Commands { get; }

    /// <summary>供应商定义注册表</summary>
    public IProviderDefinitionRegistry Providers { get; }

    /// <summary>构造 — 从 DI 容器注入所有通用 Registry</summary>
    public RegistryHub(
        IToolRegistry tools,
        IGoalRegistry goals,
        ISystemActuatorRegistry systemActuators,
        IAgentRoleRegistry agentRoles,
        ICodeIndexerRegistry codeIndexers,
        IForegroundTaskRegistry foregroundTasks,
        IFileReadListenerRegistry fileReadListeners,
        ICommandRegistry commands,
        IProviderDefinitionRegistry providers) {
        Tools = tools;
        Goals = goals;
        SystemActuators = systemActuators;
        AgentRoles = agentRoles;
        CodeIndexers = codeIndexers;
        ForegroundTasks = foregroundTasks;
        FileReadListeners = fileReadListeners;
        Commands = commands;
        Providers = providers;
    }
}
