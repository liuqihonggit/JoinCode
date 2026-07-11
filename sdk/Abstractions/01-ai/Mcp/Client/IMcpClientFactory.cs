namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpClientFactory
{
    IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null);
}
