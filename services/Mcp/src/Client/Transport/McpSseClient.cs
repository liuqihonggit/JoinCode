
namespace McpClient;

public sealed partial class McpSseClient : McpNetworkClient<Transports.SseClientTransport>
{
    protected override string TransportTypeName => "sse";

    public McpSseClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, IMcpAuthProvider? authProvider = null)
        : base(config, options, logger, authProvider,
            CreateTransport(config, authProvider, logger))
    {
    }

    private static Transports.SseClientTransport CreateTransport(
        McpServerConnectionConfig config, IMcpAuthProvider? authProvider, ILogger? logger)
    {
        IMcpAuthProvider? resolvedAuthProvider = authProvider;
        if (resolvedAuthProvider == null && config.Auth != null)
        {
            resolvedAuthProvider = McpAuthProviderFactory.Create(config.Auth, logger);
        }

        return new Transports.SseClientTransport(config, resolvedAuthProvider, logger as ILogger<Transports.SseClientTransport>);
    }
}
