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
        services.AddSingleton<IGoalEngine, GoalEngine>();
        return services;
    }
}
