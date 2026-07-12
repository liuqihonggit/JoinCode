namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 法官Agent — 最终裁决，基于 DAG 证据链
/// </summary>
public sealed class JudgeAgent : IReasoningAgent
{
    private readonly ILogger<JudgeAgent> _logger;

    public AgentRole Role => AgentRole.Judge;
    public string Name => "法官Agent";

    public JudgeAgent(ILogger<JudgeAgent> logger)
    {
        _logger = logger;
    }

    public Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "裁决" };
        var opts = context.Options;

        var pending = context.AllItems
            .Where(x => x.State is DataState.Verified or DataState.Assumption)
            .ToList();

        foreach (var item in pending)
        {
            var itemEvidenceIds = item.EvidenceIds.ToHashSet();
            var itemCounterIds = item.CounterIds.ToHashSet();

            var prosEvidence = context.AllEvidence
                .Where(e => e.SubmittedBy == AgentRole.Prosecutor && itemEvidenceIds.Contains(e.Id))
                .ToList();

            var defEvidence = context.AllEvidence
                .Where(e => e.SubmittedBy == AgentRole.Defender && itemCounterIds.Contains(e.Id))
                .ToList();

            var prosWeight = prosEvidence.Sum(e => e.Weight * (int)e.TrustLevel / 100.0);
            var defWeight = defEvidence.Sum(e => e.Weight * (int)e.TrustLevel / 100.0);

            if (prosWeight >= opts.AcceptThreshold && prosWeight > defWeight * opts.AcceptMultiplier)
            {
                action.Verdicts.Add(new Verdict
                {
                    ClaimId = item.Id,
                    Decision = VerdictDecision.Accept,
                    Reason = $"控方证据充分 (权重: {prosWeight:F2} vs {defWeight:F2})",
                    Confidence = Math.Min(100, 70 + (int)(prosWeight * 10)),
                });
            }
            else if (defWeight > prosWeight * opts.RejectMultiplier)
            {
                action.Verdicts.Add(new Verdict
                {
                    ClaimId = item.Id,
                    Decision = VerdictDecision.Reject,
                    Reason = $"辩方证据更有力 (权重: {defWeight:F2} vs {prosWeight:F2})",
                    Confidence = Math.Min(100, 60 + (int)(defWeight * 10)),
                });
            }
            else if (prosWeight > 0 && defWeight > 0 && Math.Abs(prosWeight - defWeight) < opts.PendingWeightDelta)
            {
                action.Verdicts.Add(new Verdict
                {
                    ClaimId = item.Id,
                    Decision = VerdictDecision.Pending,
                    Reason = $"控辩双方证据相当 (权重: {prosWeight:F2} vs {defWeight:F2})，需要补充证据",
                    Confidence = 50,
                });
            }
        }

        return Task.FromResult(action);
    }
}
