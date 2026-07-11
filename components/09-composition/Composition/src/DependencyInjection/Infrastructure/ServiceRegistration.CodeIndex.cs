
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddCodeIndexServices(this IServiceCollection services, string workspaceRoot)
    {
        global::Services.CodeIndex.CodeIndexServiceRegistration.AddCodeIndexServices(services, workspaceRoot);

        return services;
    }
}
