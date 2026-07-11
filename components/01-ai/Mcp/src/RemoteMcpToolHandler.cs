
namespace McpToolRegistry;

/// <summary>
/// 远程 MCP 工具处理器
/// </summary>
internal sealed class RemoteMcpToolHandler : IToolHandler
{
    private readonly string _clientId;
    private readonly IMcpClient _client;
    private readonly ToolInfo _tool;

    public string Name { get; }
    public string Description => _tool.Description ?? string.Empty;
    public ToolSchema InputSchema => _tool.InputSchema;

    public RemoteMcpToolHandler(string clientId, IMcpClient client, ToolInfo tool)
    {
        _clientId = clientId;
        _client = client;
        _tool = tool;
        Name = McpNameNormalizer.BuildMcpToolName(clientId, tool.Name);
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
            mcpProgress = progress =>
            {
                var extra = new Dictionary<string, JsonElement>
                {
                    ["serverName"] = JsonSerializer.SerializeToElement(clientId, McpClientJsonContext.Default.String),
                    ["toolName"] = JsonSerializer.SerializeToElement(toolName, McpClientJsonContext.Default.String),
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
                    ToolUseId = $"{clientId}.{toolName}",
                    Message = progress.ProgressMessage,
                    Extra = extra
                });
            };
        }

        return await _client.CallToolAsync(_tool.Name, arguments, cancellationToken, mcpProgress).ConfigureAwait(false);
    }
}
