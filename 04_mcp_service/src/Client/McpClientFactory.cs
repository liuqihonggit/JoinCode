
namespace McpClient;

[Register(typeof(IMcpClientFactory), ServiceLifetime.Singleton)]
public sealed partial class McpClientFactory : ServiceEntity, IMcpClientFactory
{
    public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.TransportType switch
        {
            McpClientTransportType.Stdio => new McpStdioClient(config, logger: logger),
            McpClientTransportType.Http => new McpHttpClient(config, logger: logger),
            McpClientTransportType.WebSocket => new McpWebSocketClient(config, logger: logger),
            _ => throw new NotSupportedException($"[MCP021] 不支持的传输类型: {config.TransportType}")
        };
    }

    public IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null)
    {
        return enableFallback ? CreateClientWithFallback(config, logger: logger) : CreateClient(config, logger);
    }

    public IMcpClient CreateClientWithFallback(
        McpServerConnectionConfig config,
        TransportFallbackConfig? fallbackConfig = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        fallbackConfig ??= TransportFallbackConfig.FromEnvironment();

        var clientChain = BuildClientFallbackChain(config, logger);
        return new McpFallbackClient(config, clientChain, fallbackConfig, logger);
    }

    private static (IMcpTransport[] Transports, ITransportHealthCheck[] HealthChecks) BuildClientFallbackChain(
        McpServerConnectionConfig config, ILogger? logger)
    {
        var transports = new List<IMcpTransport>();
        var healthChecks = new List<ITransportHealthCheck>();

        if (config.TransportType == McpClientTransportType.Stdio && !string.IsNullOrWhiteSpace(config.Endpoint))
        {
            healthChecks.Add(new StdioHealthCheck(config.Endpoint, new IO.FileSystem.PhysicalFileSystem()));
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) && config.TransportType != McpClientTransportType.Stdio)
        {
            transports.Add(new HttpTransport(config, logger: logger as ILogger<HttpTransport>));
            healthChecks.Add(new HttpListenerHealthCheck($"http://localhost:{ExtractPort(config.Endpoint)}/"));

            transports.Add(new WebSocketTransport(config));
        }

        if (transports.Count == 0)
            throw new InvalidOperationException($"Cannot build fallback chain: no transports available for config '{config.Name}'");

        return (transports.ToArray(), healthChecks.ToArray());
    }

    private static int ExtractPort(string endpoint)
    {
        try
        {
            var uri = new Uri(endpoint);
            return uri.Port > 0 ? uri.Port : 80;
        }
        catch
        {
            return 80;
        }
    }
}
