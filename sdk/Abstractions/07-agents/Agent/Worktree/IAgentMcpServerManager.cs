namespace JoinCode.Abstractions.Interfaces;

public interface IAgentMcpServerManager
{
    Task<AgentMcpServerResult> InitializeAgentMcpServersAsync(
        JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition agentDefinition,
        IReadOnlyList<string>? parentClientIds = null,
        CancellationToken cancellationToken = default);

    Task CleanupAgentMcpServersAsync(string agentId, CancellationToken cancellationToken = default);
}

public sealed class AgentMcpServerResult
{
    public required string AgentId { get; init; }
    public List<McpConnectedServer> ConnectedServers { get; set; } = [];
    public List<string> ToolNames { get; set; } = [];
}

public sealed class McpConnectedServer
{
    public required string ServerName { get; init; }
    public required string ClientId { get; init; }
    public required bool IsNewlyCreated { get; init; }
}
