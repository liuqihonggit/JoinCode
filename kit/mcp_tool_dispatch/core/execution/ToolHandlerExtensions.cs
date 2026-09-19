
namespace McpToolDispatch;

/// <summary>
/// 工具分发服务注册扩展方法集合
/// </summary>
public static class ToolHandlerExtensions {
    /// <summary>
    /// 注册 MCP 工具分发所需的全部单例服务 — 委托源码生成器生成的注册方法
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>原服务集合，便于链式调用</returns>
    public static IServiceCollection AddMcpToolDispatch(this IServiceCollection services) {
        GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.AddMcpToolDispatchSingletons(services);
        return services;
    }

    /// <summary>
    /// 将全部生成的工具处理器注册到 MCP 工具注册表 — 委托源码生成器生成的注册方法
    /// </summary>
    /// <param name="registry">MCP 工具注册表</param>
    /// <param name="serviceProvider">服务提供者，用于解析处理器依赖</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注册完成后的工具注册表</returns>
    public static async Task<IMcpToolRegistry> RegisterAllToolDispatchAsync(
        this IMcpToolRegistry registry,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default) {
        var result = await GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.RegisterAllMcpToolDispatchAsync(registry, serviceProvider, cancellationToken);
        return result;
    }
}