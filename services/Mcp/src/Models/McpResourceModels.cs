
namespace McpClient.Models;

public class McpToolsListResponse
{
    [JsonPropertyName("tools")]
    public List<ToolInfo> Tools { get; init; } = new();
}
