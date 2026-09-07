namespace JoinCode.Hands.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddApiClientServices(this IServiceCollection services)
    {
        services.AddOptions<ApiSettings>();
        services.AddSingleton(new VcrOptions());
        return services;
    }

    public static IServiceCollection AddCodeSecurityServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddSkillServices(this IServiceCollection services)
    {
        return services;
    }
}
