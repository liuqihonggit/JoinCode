
namespace McpToolHandlers;

public static class ToolHandlerExtensions
{
    public static IServiceCollection AddMcpToolHandlers(this IServiceCollection services)
    {
        GeneratedToolHandlerRegistration_JoinCode_McpToolHandlers.AddMcpToolHandlerSingletons(services);
        return services;
    }

    public static async Task<IMcpToolRegistry> RegisterAllToolHandlersAsync(
        this IMcpToolRegistry registry,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var result = await GeneratedToolHandlerRegistration_JoinCode_McpToolHandlers.RegisterAllMcpToolHandlersAsync(registry, serviceProvider, cancellationToken);
        return result;
    }
}
