namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 辩方Agent — 质疑证据，寻找反驳
/// </summary>
public sealed class DefenderAgent : IReasoningAgent
{
    private readonly ILogger<DefenderAgent> _logger;

    public AgentRole Role => AgentRole.Defender;
    public string Name => "辩方Agent";

    public DefenderAgent(ILogger<DefenderAgent> logger)
    {
        _logger = logger;
    }

    public Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "质疑证据" };
        var doubtThreshold = context.Options.DefenderDoubtThreshold;

        var verified = context.AllItems
            .Where(x => x.State == DataState.Verified)
            .ToList();

        foreach (var item in verified)
        {
            _logger.LogDebug("[辩方] 质疑已验证项: {Content}", item.Content);
            action.AffectedClaimIds.Add(item.Id);

            var itemEvidenceIds = item.EvidenceIds.ToHashSet();
            var supportingEvidence = context.AllEvidence
                .Count(e => e.SubmittedBy == AgentRole.Prosecutor && itemEvidenceIds.Contains(e.Id));

            if (supportingEvidence < doubtThreshold)
            {
                action.Doubts.Add($"证据链不完整: {item.Content}");
                action.ActionType = "质疑";
            }
        }

        return Task.FromResult(action);
    }
}
