
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddCodeIndexServices(this IServiceCollection services, string workspaceRoot)
    {
        global::JoinCode.Eyes.DependencyInjection.ServiceRegistration.AddCodeIndexServices(services, workspaceRoot);

        return services;
    }
}
