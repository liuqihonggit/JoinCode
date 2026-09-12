
namespace McpClient;

public sealed partial class McpWebSocketClient : McpNetworkClient<Transports.WebSocketTransport>
{
    protected override string TransportTypeName => "websocket";

    public McpWebSocketClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, IMcpAuthProvider? authProvider = null)
        : base(config, options, logger, authProvider,
            CreateTransport(config, authProvider, logger))
    {
    }

    private static Transports.WebSocketTransport CreateTransport(
        McpServerConnectionConfig config, IMcpAuthProvider? authProvider, ILogger? logger)
    {
        IMcpAuthProvider? resolvedAuthProvider = authProvider;
        if (resolvedAuthProvider == null && config.Auth != null)
        {
            resolvedAuthProvider = McpAuthProviderFactory.Create(config.Auth, logger);
        }

        return new Transports.WebSocketTransport(config, resolvedAuthProvider, logger as ILogger<Transports.WebSocketTransport>);
    }
}
