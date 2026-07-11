namespace Core.DependencyInjection;

/// <summary>
/// Agents 子系统的 DI 注册扩展方法
/// </summary>
public static class AgentServiceRegistration
{
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        services.AddSingleton<Lazy<IWorktreePipelineOperations>>(sp => new Lazy<IWorktreePipelineOperations>(sp.GetRequiredService<IWorktreePipelineOperations>));
        return services;
    }

    public static IServiceCollection AddReleaseModeAgentServices(this IServiceCollection services)
    {
        services.AddAgentCoordinatorServices();

        return services;
    }

    public static IServiceCollection AddAgentCoordinatorServices(this IServiceCollection services)
    {
        // IAgentWorktreeManager — [Register] 自动注册
        // AgentCoreDependencies — [Register] 自动注册
        // AgentPermissionDependencies — [Register] 自动注册
        // AgentTeamDependencies — [Register] 自动注册
        // AgentCoordinator — [Register] + [Register] 自动注册

        // IPaneBackend — [Register] 自动注册（PaneBackendSelector，运行时选择 Tmux/iTerm2/InProcess）

        // Spawn 中间件 — [Register] 源码生成器自动注册
        // AgentSpawn 管道 — 由 [RegisterMiddleware] + 生成器自动注册

        // IAgentService — [Register] 自动注册（AgentServiceImpl）
        // AgentServiceDependencies — [Register] 自动注册（init 属性 record，DI 通过构造函数解析）

        // Fork 中间件 — [Register] 源码生成器自动注册
        // Fork 管道 — 由 [RegisterMiddleware] + 生成器自动注册
        // IForkSubAgentManager — [Register] 自动注册（ForkSubAgentManager）
        // ForkManagerDependencies — [Register] 自动注册（主构造函数参数均为 DI 接口）

        return services;
    }
}
