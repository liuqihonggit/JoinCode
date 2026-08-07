namespace JoinCode.Clock.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddClockServices(this IServiceCollection services)
    {
        services.AddGoalServices();
        return services;
    }

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
