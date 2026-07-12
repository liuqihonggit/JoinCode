namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 控方Agent — 主动寻找证据，提出指控
/// </summary>
public sealed class ProsecutorAgent : IReasoningAgent
{
    private readonly ILogger<ProsecutorAgent> _logger;

    public AgentRole Role => AgentRole.Prosecutor;
    public string Name => "控方Agent";

    public ProsecutorAgent(ILogger<ProsecutorAgent> logger)
    {
        _logger = logger;
    }

    public Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "提出证据" };

        var unverified = context.AllItems
            .Where(x => x.State == DataState.Assumption)
            .ToList();

        foreach (var item in unverified)
        {
            _logger.LogDebug("[控方] 审查假定: {Content}", item.Content);
            action.AffectedClaimIds.Add(item.Id);
        }

        return Task.FromResult(action);
    }
}
