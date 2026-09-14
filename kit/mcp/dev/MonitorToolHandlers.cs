

namespace McpToolDispatch;

/// <summary>
/// MCP 监控工具处理器 — 提供 MCP 服务器状态、工具列表、客户端列表、健康检查等监控功能
/// </summary>
[McpToolDispatch(ToolCategory.Monitor, Optional = true)]
public partial class MonitorToolHandlers
{
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly ILogger<MonitorToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="MonitorToolHandlers"/> 实例
    /// </summary>
    /// <param name="toolRegistry">MCP 工具注册表</param>
    /// <param name="logger">日志记录器（可选）</param>
    public MonitorToolHandlers(IMcpToolRegistry toolRegistry, ILogger<MonitorToolHandlers>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    /// <summary>
    /// 监控 MCP 服务器状态和工具调用
    /// </summary>
    /// <param name="monitor_type">监控类型：status/tools/clients/health（默认 status）</param>
    /// <param name="client_id">客户端 ID（可选，用于指定客户端）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(SystemToolNameEnumConstants.Monitor, "Monitor MCP server status and tool calls", "mcp")]
    public async Task<ToolResult> MonitorMcpAsync(
        [McpToolParameter("Monitor type: status/tools/clients/health (default status)", Required = false)] string monitor_type = "status",
        [McpToolParameter("Client ID (optional, for specific client)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        var monitorType = MonitorTypeExtensions.FromValue(monitor_type);
        if (monitorType == null)
            return ToolResultBuilder.Error().WithText(L.T(StringKey.MonitorUnknownType, monitor_type)).Build();

        try
        {
            var response = new System.Text.StringBuilder();

            switch (monitorType.Value)
            {
                case MonitorType.Status:
                    var localCount = await _toolRegistry.GetLocalToolCountAsync(cancellationToken).ConfigureAwait(false);
                    var remoteCount = await _toolRegistry.GetRemoteClientCountAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.MonitorStatusOverview));
                    response.AppendLine();
                    response.AppendLine(L.T(StringKey.MonitorLocalToolCount, localCount));
                    response.AppendLine(L.T(StringKey.MonitorRemoteClientCount, remoteCount));
                    break;

                case MonitorType.Tools:
                    var allTools = await _toolRegistry.GetAllToolsAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.MonitorRegisteredTools, allTools.Count));
                    response.AppendLine();
                    foreach (var tool in allTools.OrderBy(t => t.Key))
                    {
                        response.AppendLine($"  {tool.Key}: {tool.Value.Description}");
                    }
                    break;

                case MonitorType.Clients:
                    var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken).ConfigureAwait(false);
                    response.AppendLine(L.T(StringKey.MonitorRemoteClients, clients.Count));
                    response.AppendLine();
                    foreach (var client in clients)
                    {
                        var serverName = client.Value.ServerInfo?.Name ?? L.T(StringKey.MonitorUnknown);
                        response.AppendLine($"  {client.Key}: {serverName}");
                    }
                    break;

                case MonitorType.Health:
                    if (!string.IsNullOrEmpty(client_id))
                    {
                        var client = await _toolRegistry.GetRemoteClientAsync(client_id, cancellationToken).ConfigureAwait(false);
                        if (client == null)
                            return ToolResultBuilder.Error().WithText(L.T(StringKey.MonitorClientNotFound, client_id)).Build();
                        response.AppendLine(L.T(StringKey.MonitorClientHealthCheck, client_id));
                        response.AppendLine(L.T(StringKey.LabelServer, client.ServerInfo?.Name ?? L.T(StringKey.MonitorUnknown)));
                        response.AppendLine(L.T(StringKey.LabelStatus, client.IsConnected ? L.T(StringKey.MonitorConnected) : L.T(StringKey.MonitorDisconnected)));
                    }
                    else
                    {
                        response.AppendLine(L.T(StringKey.MonitorHealthCheck));
                        response.AppendLine();
                        var allClients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken).ConfigureAwait(false);
                        foreach (var c in allClients)
                        {
                            response.AppendLine($"  {c.Key}: {(c.Value.IsConnected ? L.T(StringKey.MonitorConnected) : L.T(StringKey.MonitorDisconnected))}");
                        }
                        if (allClients.Count == 0)
                            response.AppendLine($"  {L.T(StringKey.MonitorNoRemoteClients)}");
                    }
                    break;
            }

            return ToolResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.MonitorFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.MonitorFailed, ex.Message)).Build();
        }
    }
}
