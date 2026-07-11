

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Monitor, Optional = true)]
public partial class MonitorToolHandlers
{
    private readonly IMcpToolRegistry _toolRegistry;
    [Inject] private readonly ILogger<MonitorToolHandlers>? _logger;

    public MonitorToolHandlers(IMcpToolRegistry toolRegistry, ILogger<MonitorToolHandlers>? logger = null)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.Monitor, "Monitor MCP server status and tool calls", "mcp")]
    public async Task<ToolResult> MonitorMcpAsync(
        [McpToolParameter("Monitor type: status/tools/clients/health (default status)", Required = false)] string monitor_type = "status",
        [McpToolParameter("Client ID (optional, for specific client)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        var monitorType = MonitorTypeExtensions.FromValue(monitor_type);
        if (monitorType == null)
            return McpResultBuilder.Error().WithText(L.T(StringKey.MonitorUnknownType, monitor_type)).Build();

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
                            return McpResultBuilder.Error().WithText(L.T(StringKey.MonitorClientNotFound, client_id)).Build();
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

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.MonitorFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.MonitorFailed, ex.Message)).Build();
        }
    }
}
