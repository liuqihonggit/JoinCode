
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddKernelWithPlugins(
        this IServiceCollection services,
        WorkflowConfig config)
    {
        ApiRegistration.AddKernelWithPlugins(services, config.Provider, config.PipeEndpoint);

        // PluginManager — auto-registered via [Register] (both IPluginManager and self-type)

        return services;
    }

    public static IServiceCollection AddKernelWithDynamicPlugins(
        this IServiceCollection services,
        WorkflowConfig config)
    {
        ApiRegistration.AddKernelWithDynamicPlugins(services, config.Provider);

        // PluginManager — auto-registered via [Register] (both IPluginManager and self-type)

        return services;
    }
}
