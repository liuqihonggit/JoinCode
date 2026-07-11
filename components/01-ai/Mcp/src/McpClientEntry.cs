
namespace McpToolRegistry;

/// <summary>
/// MCP 客户端条目
/// </summary>
internal sealed class McpClientEntry
{
    public required string ClientId { get; init; }
    public required IMcpClient Client { get; init; }
    public DateTime RegisteredAt { get; init; }
}
