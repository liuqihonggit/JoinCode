namespace Core.Agents;

/// <summary>
/// MCP 服务器初始化中间件 — 初始化 Agent 定义中的 MCP 服务器
/// 合并自路径 A 的 McpSetupMiddleware
/// </summary>
[Register(typeof(IUnifiedSpawnMiddleware))]
public sealed partial class McpSetupMiddleware : ServiceEntity, IUnifiedSpawnMiddleware
{

    public McpSetupMiddleware(IAgentMcpServerManager? mcpServerManager = null, ILogger<McpSetupMiddleware>? logger = null)
    {
        _mcpServerManager = mcpServerManager;
        _logger = logger;
    }
    private readonly IAgentMcpServerManager? _mcpServerManager;
    private readonly ILogger<McpSetupMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(UnifiedSpawnContext context, MiddlewareDelegate<UnifiedSpawnContext> next, CancellationToken ct)
    {
        if (_mcpServerManager is not null && context.Agent is not null)
        {
            await InitializeMcpServersIfNeededAsync(context.AgentId, context.Definition, ct).ConfigureAwait(false);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private async Task InitializeMcpServersIfNeededAsync(string agentId, JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition? definition, CancellationToken cancellationToken)
    {
        if (definition is null) return;
        if (definition.McpServers is null or { Count: 0 }) return;

        try
        {
            var result = await (_mcpServerManager ?? throw new InvalidOperationException("McpServerManager not available")).InitializeAgentMcpServersAsync(definition, null, cancellationToken).ConfigureAwait(false);

            if (result.ConnectedServers.Count > 0)
            {
                _logger?.LogInformation("[McpSetupMiddleware] Agent {AgentId} 已连接 {Count} 个 MCP 服务器，可用工具: {ToolCount}",
                    agentId, result.ConnectedServers.Count, result.ToolNames.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[McpSetupMiddleware] Agent {AgentId} MCP 服务器初始化失败", agentId);
        }
    }
}
