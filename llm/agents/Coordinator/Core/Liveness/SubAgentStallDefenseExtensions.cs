namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 子代理卡死防护 DI 注册扩展 — 在 Agents 项目内部注册,可访问 internal 事件（ADR 0106）
/// </summary>
public static class SubAgentStallDefenseExtensions
{
    /// <summary>
    /// 注册子代理卡死防护纵深防御体系所有组件
    /// </summary>
    /// <param name="services">DI 容器</param>
    /// <returns>已注册服务的 IServiceCollection 实例</returns>
    public static IServiceCollection AddSubAgentStallDefense(this IServiceCollection services)
    {
        // SubAgentPool — 代理池（L3 抢塞）
        services.AddSingleton<SubAgentPool>(sp =>
        {
            var options = sp.GetRequiredService<SubAgentLivenessOptions>();
            var pool = new SubAgentPool(options);
            pool.Start();
            return pool;
        });

        // SubAgentLivenessScanner — 后台扫描器（L2 检测）
        services.AddSingleton<SubAgentLivenessScanner>(sp =>
        {
            var stateMachine = sp.GetRequiredService<AgentStateMachine>();
            var lifecycle = sp.GetRequiredService<IAgentLifecycleManager>();
            var fork = sp.GetRequiredService<IForkSubAgentManager>();
            var options = sp.GetRequiredService<SubAgentLivenessOptions>();
            return new SubAgentLivenessScanner(stateMachine, lifecycle, fork, options);
        });

        // SubAgentActivator — 激活动作（L3 干预）
        services.AddSingleton<SubAgentActivator>(sp =>
        {
            var lifecycle = sp.GetRequiredService<IAgentLifecycleManager>();
            return new SubAgentActivator(lifecycle);
        });

        // ProgressiveCompactor — 渐进式压缩（L4 恢复）
        services.AddSingleton<ProgressiveCompactor>(sp =>
        {
            var contextManager = sp.GetRequiredService<IChatContextManager>();
            return new ProgressiveCompactor(contextManager);
        });

        // PreemptiveScheduler — 抢占式调度器（L3 抢塞）
        services.AddSingleton<PreemptiveScheduler>(sp =>
        {
            var pool = sp.GetRequiredService<SubAgentPool>();
            var contextManager = sp.GetRequiredService<IChatContextManager>();
            var options = sp.GetRequiredService<SubAgentLivenessOptions>();
            return new PreemptiveScheduler(pool, contextManager, options);
        });

        // SubAgentStallDefenseCoordinator — 纵深防御协调器（集成层）
        services.AddSingleton<SubAgentStallDefenseCoordinator>(sp =>
        {
            var scanner = sp.GetRequiredService<SubAgentLivenessScanner>();
            var activator = sp.GetRequiredService<SubAgentActivator>();
            var compactor = sp.GetRequiredService<ProgressiveCompactor>();
            var preemptive = sp.GetRequiredService<PreemptiveScheduler>();
            var options = sp.GetRequiredService<SubAgentLivenessOptions>();
            var coordinator = new SubAgentStallDefenseCoordinator(scanner, activator, compactor, preemptive, options);
            coordinator.Start();
            return coordinator;
        });

        return services;
    }
}
