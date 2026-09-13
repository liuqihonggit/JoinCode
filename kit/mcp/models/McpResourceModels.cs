
namespace McpClient.Models;

/// <summary>
/// MCP 工具列表响应 — 表示 tools/list 请求的响应
/// </summary>
public class McpToolsListResponse
{
    /// <summary>
    /// 工具信息列表
    /// </summary>
    [JsonPropertyName("tools")]
    public List<ToolInfo> Tools { get; init; } = new();
}
