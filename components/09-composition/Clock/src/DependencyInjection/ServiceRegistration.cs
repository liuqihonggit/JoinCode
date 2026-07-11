namespace Clock.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddClockServices(this IServiceCollection services)
    {
        services.AddGoalServices();
        return services;
    }
}
