namespace Core.Agents;

/// <summary>
/// MCP 服务器初始化中间件 — 初始化 Agent 定义中的 MCP 服务器
/// </summary>
[Register]
public sealed partial class McpSetupMiddleware : IAgentSpawnMiddleware
{
    [Inject] private readonly JoinCode.Abstractions.Interfaces.IAgentMcpServerManager? _mcpServerManager;
    [Inject] private readonly ILogger<McpSetupMiddleware>? _logger;

    /// <summary>MCP 初始化在 Hook 注册之后</summary>

    /// <summary>MCP 初始化失败不应中断管道</summary>
    public JoinCode.Abstractions.Pipeline.ErrorBehavior OnError => JoinCode.Abstractions.Pipeline.ErrorBehavior.Continue;

    public async Task InvokeAsync(AgentSpawnContext context, JoinCode.Abstractions.Pipeline.MiddlewareDelegate<AgentSpawnContext> next, CancellationToken ct)
    {
        if (_mcpServerManager is not null && context.SubAgent is not null)
        {
            await InitializeMcpServersIfNeededAsync(context.SubAgent.Id, context.Definition, ct).ConfigureAwait(false);
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
