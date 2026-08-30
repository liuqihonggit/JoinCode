namespace McpToolDispatch;

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
