namespace JoinCode.Eyes.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddCodeIndexServices(
        this IServiceCollection services,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        return services;
    }
}
