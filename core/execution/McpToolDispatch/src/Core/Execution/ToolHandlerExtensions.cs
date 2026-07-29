
namespace McpToolDispatch;

public static class ToolHandlerExtensions
{
    public static IServiceCollection AddMcpToolDispatch(this IServiceCollection services)
    {
        GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.AddMcpToolDispatchSingletons(services);
        return services;
    }

    public static async Task<IMcpToolRegistry> RegisterAllToolDispatchAsync(
        this IMcpToolRegistry registry,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var result = await GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.RegisterAllMcpToolDispatchAsync(registry, serviceProvider, cancellationToken);
        return result;
    }
}
