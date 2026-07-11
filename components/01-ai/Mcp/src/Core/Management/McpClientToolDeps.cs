using JoinCode.Abstractions.Attributes;

namespace McpToolHandlers;

/// <summary>
/// McpClientToolHandlers 可选依赖聚合
/// </summary>
[Register]
public sealed record McpClientToolDeps(
    McpOAuthService? OAuthService = null,
    IMcpOutputStorage? OutputStorage = null,
    IImageResizeService? ImageResizer = null,
    McpAuthToolHandlers? AuthToolHandlers = null,
    IMcpToolRegistry? ToolRegistry = null,
    IElicitationHandler? ElicitationHandler = null,
    McpServerStateManager? ServerStateManager = null)
{
    /// <summary>
    /// 从 DI 服务提供者解析所有可选依赖
    /// </summary>
    public static McpClientToolDeps FromServiceProvider(IServiceProvider sp)
    {
        return new McpClientToolDeps(
            OAuthService: sp.GetService<McpOAuthService>(),
            OutputStorage: sp.GetService<IMcpOutputStorage>(),
            ImageResizer: sp.GetService<IImageResizeService>(),
            AuthToolHandlers: sp.GetService<McpAuthToolHandlers>(),
            ToolRegistry: sp.GetService<IMcpToolRegistry>(),
            ElicitationHandler: sp.GetService<IElicitationHandler>(),
            ServerStateManager: sp.GetService<McpServerStateManager>());
    }
}
