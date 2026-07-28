
namespace McpClient;

public sealed partial class McpHttpClient : McpNetworkClient<Transports.HttpTransport>
{
    protected override string TransportTypeName => "http";

    public McpHttpClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, IMcpAuthProvider? authProvider = null)
        : base(config, options, logger, authProvider,
            CreateTransport(config, authProvider, logger))
    {
    }

    private static Transports.HttpTransport CreateTransport(
        McpServerConnectionConfig config, IMcpAuthProvider? authProvider, ILogger? logger)
    {
        IMcpAuthProvider? resolvedAuthProvider = authProvider;
        if (resolvedAuthProvider == null && config.Auth != null)
        {
            resolvedAuthProvider = McpAuthProviderFactory.Create(config.Auth, logger);
        }

        return new Transports.HttpTransport(config, resolvedAuthProvider, logger as ILogger<Transports.HttpTransport>);
    }
}
