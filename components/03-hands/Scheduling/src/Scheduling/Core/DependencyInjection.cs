
namespace Core.Scheduling;

public static class SchedulingDependencyInjectionExtensions
{
    public static IServiceCollection AddSchedulingServices(this IServiceCollection services)
    {
        services.AddCronServices();

        return services;
    }

    public static IServiceCollection AddCronServices(this IServiceCollection services, string? tasksDirectory = null)
    {
        return services;
    }
}
