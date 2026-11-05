namespace JoinCode.Abstractions.Interfaces;

public interface IAgentMcpServerManager {
    /// <summary>初始化代理 MCP 服务器。</summary>
    /// <param name="agentDefinition">代理定义。</param>
    /// <param name="parentClientIds">父级客户端标识列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<AgentMcpServerResult> InitializeAgentMcpServersAsync(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition agentDefinition,
        IReadOnlyList<string>? parentClientIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>清理代理 MCP 服务器。</summary>
    /// <param name="agentId">代理标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CleanupAgentMcpServersAsync(string agentId, CancellationToken cancellationToken = default);
}

public sealed class AgentMcpServerResult {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取已连接服务器列表。</summary>
    public List<McpConnectedServer> ConnectedServers { get; set; } = [];
    /// <summary>获取工具名称列表。</summary>
    public List<string> ToolNames { get; set; } = [];
}

public sealed class McpConnectedServer {
    /// <summary>获取服务器名称。</summary>
    public required string ServerName { get; init; }
    /// <summary>获取客户端标识。</summary>
    public required string ClientId { get; init; }
    /// <summary>获取是否为新创建的服务器。</summary>
    public required bool IsNewlyCreated { get; init; }
}