namespace Core.Agents.Coordinator;

public sealed class AgentSpawnCoordContext : IPipelineContext
{
    public required string Task { get; init; }
    public SubAgentOptions? Options { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public ISubAgent? Agent { get; set; }
    public string AgentId => Agent?.Id ?? string.Empty;
    public string? SessionId { get; set; }
    public bool WorktreeCreated { get; set; }
    public bool MessageRegistered { get; set; }
    public DateTime SpawnedAt { get; set; }
    public AgentExecutionContext? ExecutionContext { get; set; }
    public bool PermissionRoutingEnsured { get; set; }
    public bool PlanApprovalRoutingStarted { get; set; }
    public bool TeammatePaneCreated { get; set; }

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
