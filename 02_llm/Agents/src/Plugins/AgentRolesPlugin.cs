namespace Core.Agents;

/// <summary>
/// Agent 角色插件 — 注册内置 AgentRoleProfile(Captain/Executor/Doctor 等)
/// <para>万物皆插件(ADR 0098): 从 AgentRoleProfileRegistry 构造时硬编码迁移为插件加载</para>
/// <para>卸载时撤销内置 Profile,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class AgentRolesPlugin : WorkflowPluginBase
{
    private AgentRoleProfileRegistry? _registry;

    public AgentRolesPlugin() : base("AgentRoles") { }

    /// <summary>插件名称</summary>
    public override string Name => "AgentRoles";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "Agent角色Profile插件(Captain/Executor/Doctor等)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 从 DI 获取 AgentRoleProfileRegistry,注册内置角色</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _registry = serviceProvider.GetRequiredService<AgentRoleProfileRegistry>();
        _registry.RegisterBuiltInProfiles();
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 撤销内置角色 Profile</summary>
    protected override void OnUnload()
    {
        _registry?.UnregisterBuiltInProfiles();
        _registry = null;
    }
}
