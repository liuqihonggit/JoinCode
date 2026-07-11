
namespace McpClient;

[Register]
public sealed class McpClientFactory : IMcpClientFactory
{
    public IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.TransportType switch
        {
            McpClientTransportType.Stdio => new McpStdioClient(config, logger: logger),
            McpClientTransportType.Sse => new McpSseClient(config, logger: logger),
            McpClientTransportType.Http => new McpHttpClient(config, logger: logger),
            McpClientTransportType.WebSocket => new McpWebSocketClient(config, logger: logger),
            _ => throw new NotSupportedException($"不支持的传输类型: {config.TransportType}")
        };
    }
}
