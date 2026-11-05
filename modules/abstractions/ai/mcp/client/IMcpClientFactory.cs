namespace JoinCode.Abstractions.Mcp.Client;

public interface IMcpClientFactory {
    /// <summary>根据配置创建 MCP 客户端。</summary>
    IMcpClient CreateClient(McpServerConnectionConfig config, ILogger? logger = null);

    /// <summary>根据配置创建 MCP 客户端,可启用回退机制。</summary>
    IMcpClient CreateClient(McpServerConnectionConfig config, bool enableFallback, ILogger? logger = null);
}
