
namespace McpToolRegistry;

/// <summary>
/// MCP 客户端条目
/// </summary>
internal sealed class McpClientEntry {
    /// <summary>获取客户端标识。</summary>
    public required string ClientId { get; init; }
    /// <summary>获取 MCP 客户端实例。</summary>
    public required IMcpClient Client { get; init; }
    /// <summary>获取注册时间。</summary>
    public DateTime RegisteredAt { get; init; }
}