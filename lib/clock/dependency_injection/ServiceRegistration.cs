namespace JoinCode.Clock.DependencyInjection;

/// <summary>
/// Clock 工程服务注册静态类 — 提供 AddClockServices/AddGoalServices 扩展方法
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册 Clock 工程全部服务 — 等价于调用 AddGoalServices
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，供链式调用</returns>
    public static IServiceCollection AddClockServices(this IServiceCollection services)
    {
        services.AddGoalServices();
        return services;
    }

    /// <summary>
    /// 注册 Goal 引擎相关服务 — 评估器、心跳、模板注册表、引擎等
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，供链式调用</returns>
    public static IServiceCollection AddGoalServices(this IServiceCollection services)
    {
        services.AddSingleton<IGoalEvaluator, GoalEvaluator>();
        services.AddSingleton<IGoalHeartbeat, GoalHeartbeat>();
        services.AddSingleton<IGoalGraphTemplateRegistry, GoalGraphTemplateRegistry>();
        services.AddSingleton<IGoalEngine, GoalEngine>();
        services.AddSingleton<IAgentRunner>(static sp => (IAgentRunner)sp.GetRequiredService<IGoalEngine>());
        services.AddSingleton<IGoalEnginePostConfigure, GoalEnginePostConfigure>();
        return services;
    }
}
