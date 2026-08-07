
namespace McpToolRegistry;

/// <summary>
/// 远程 MCP 工具处理器
/// </summary>
internal sealed class RemoteMcpToolDispatch : IToolHandler
{
    private readonly string _clientId;
    private readonly IMcpClient _client;
    private readonly ToolInfo _tool;

    public string Name { get; }
    public string Description => _tool.Description ?? string.Empty;
    public ToolSchema InputSchema => _tool.InputSchema;
    public ToolKind Kind => ToolKind.Mcp;
    public string? GroupName { get; }

    public RemoteMcpToolDispatch(string clientId, IMcpClient client, ToolInfo tool, string? groupName = null)
    {
        _clientId = clientId;
        _client = client;
        _tool = tool;
        Name = McpNameNormalizer.BuildMcpToolName(clientId, tool.Name);
        GroupName = groupName;
    }

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        McpProgressCallback? mcpProgress = null;
        if (onProgress is not null)
        {
            var clientId = _clientId;
            var toolName = _tool.Name;
            var serverNameElement = JsonSerializer.SerializeToElement(clientId, McpClientJsonContext.Default.String);
            var toolNameElement = JsonSerializer.SerializeToElement(toolName, McpClientJsonContext.Default.String);
            var toolUseId = $"{clientId}.{toolName}";
            mcpProgress = progress =>
            {
                var extra = new Dictionary<string, JsonElement>
                {
                    ["serverName"] = serverNameElement,
                    ["toolName"] = toolNameElement,
                    ["status"] = JsonSerializer.SerializeToElement(progress.Status, McpClientJsonContext.Default.String),
                };
                if (progress.Progress.HasValue)
                {
                    extra["progress"] = JsonSerializer.SerializeToElement(progress.Progress.Value, McpClientJsonContext.Default.Double);
                }
                if (progress.Total.HasValue)
                {
                    extra["total"] = JsonSerializer.SerializeToElement(progress.Total.Value, McpClientJsonContext.Default.Double);
                }

                onProgress(new ToolProgressData
                {
                    ProgressType = progress.Type,
                    ToolUseId = toolUseId,
                    Message = progress.ProgressMessage,
                    Extra = extra
                });
            };
        }

        return await _client.CallToolAsync(_tool.Name, arguments, cancellationToken, mcpProgress).ConfigureAwait(false);
    }
}
