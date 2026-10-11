
namespace McpToolRegistry;

/// <summary>
/// 远程 MCP 工具处理器
/// </summary>
internal sealed class RemoteMcpToolDispatch : IToolHandler {
    private readonly string _clientId;
    private readonly IMcpClient _client;
    private readonly ToolInfo _tool;

    /// <summary>获取工具名称</summary>
    public string Name { get; }
    /// <summary>获取工具描述</summary>
    public string Description => _tool.Description ?? string.Empty;
    /// <summary>获取工具输入模式</summary>
    public ToolSchema InputSchema => _tool.InputSchema;
    /// <summary>获取工具类型</summary>
    public ToolKind Kind => ToolKind.Mcp;
    /// <summary>获取工具组名</summary>
    public string? GroupName { get; }
    /// <summary>获取工具分类</summary>
    public string? Category { get; } = "mcp_client";
    /// <summary>获取超时策略</summary>
    public ToolTimeoutPolicy TimeoutPolicy => ToolTimeoutPolicy.None;

    /// <summary>
    /// 初始化远程 MCP 工具处理器
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="client">MCP 客户端</param>
    /// <param name="tool">工具信息</param>
    /// <param name="groupName">工具组名（可选）</param>
    public RemoteMcpToolDispatch(string clientId, IMcpClient client, ToolInfo tool, string? groupName = null) {
        _clientId = clientId;
        _client = client;
        _tool = tool;
        Name = McpNameNormalizer.BuildMcpToolName(clientId, tool.Name);
        GroupName = groupName;
    }

    /// <summary>
    /// 执行远程 MCP 工具调用
    /// </summary>
    /// <param name="arguments">调用参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onProgress">进度回调（可选）</param>
    /// <returns>工具执行结果</returns>
    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null) {
        McpProgressCallback? mcpProgress = null;
        if (onProgress is not null) {
            var clientId = _clientId;
            var toolName = _tool.Name;
            var serverNameElement = JsonSerializer.SerializeToElement(clientId, McpClientJsonContext.Default.String);
            var toolNameElement = JsonSerializer.SerializeToElement(toolName, McpClientJsonContext.Default.String);
            var toolUseId = $"{clientId}.{toolName}";
            mcpProgress = progress => {
                var extra = new Dictionary<string, JsonElement> {
                    ["serverName"] = serverNameElement,
                    ["toolName"] = toolNameElement,
                    ["status"] = JsonSerializer.SerializeToElement(progress.Status, McpClientJsonContext.Default.String),
                };
                if (progress.Progress.HasValue) {
                    extra["progress"] = JsonSerializer.SerializeToElement(progress.Progress.Value, McpClientJsonContext.Default.Double);
                }
                if (progress.Total.HasValue) {
                    extra["total"] = JsonSerializer.SerializeToElement(progress.Total.Value, McpClientJsonContext.Default.Double);
                }

                onProgress(new ToolProgressData {
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