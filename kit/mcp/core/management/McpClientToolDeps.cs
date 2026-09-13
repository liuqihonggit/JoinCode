namespace McpToolDispatch;

/// <summary>
/// MCP 客户端工具依赖项集合 — 通过依赖注入聚合 MCP 客户端所需的各项服务
/// </summary>
/// <param name="OAuthService">OAuth 认证服务（可选）</param>
/// <param name="OutputStorage">MCP 输出存储服务（可选，用于持久化二进制内容）</param>
/// <param name="ImageResizer">图片缩放服务（可选，用于对大图降采样）</param>
/// <param name="AuthToolHandlers">MCP 认证工具处理器（可选，用于查询已配置的认证）</param>
/// <param name="ToolRegistry">MCP 工具注册表（可选，用于同步远程工具）</param>
/// <param name="ElicitationHandler">Elicitation 处理器（可选，用于服务器反向请求用户输入）</param>
/// <param name="ServerStateManager">MCP 服务器状态管理器（可选，用于查询禁用/启用状态）</param>
/// <param name="ClientFactory">MCP 客户端工厂（可选，用于按传输类型创建客户端）</param>
[Register(typeof(McpClientToolDeps), ServiceLifetime.Singleton)]
public sealed record McpClientToolDeps(
    McpOAuthService? OAuthService = null,
    IMcpOutputStorage? OutputStorage = null,
    IImageResizeService? ImageResizer = null,
    McpAuthToolHandlers? AuthToolHandlers = null,
    IMcpToolRegistry? ToolRegistry = null,
    IElicitationHandler? ElicitationHandler = null,
    McpServerStateManager? ServerStateManager = null,
    IMcpClientFactory? ClientFactory = null)
{
    /// <summary>
    /// 从服务提供者构造依赖项集合
    /// </summary>
    /// <param name="sp">服务提供者</param>
    /// <returns>从服务提供者解析得到的依赖项集合</returns>
    public static McpClientToolDeps FromServiceProvider(IServiceProvider sp)
    {
        return new McpClientToolDeps(
            OAuthService: sp.GetService<McpOAuthService>(),
            OutputStorage: sp.GetService<IMcpOutputStorage>(),
            ImageResizer: sp.GetService<IImageResizeService>(),
            AuthToolHandlers: sp.GetService<McpAuthToolHandlers>(),
            ToolRegistry: sp.GetService<IMcpToolRegistry>(),
            ElicitationHandler: sp.GetService<IElicitationHandler>(),
            ServerStateManager: sp.GetService<McpServerStateManager>(),
            ClientFactory: sp.GetService<IMcpClientFactory>());
    }
}
