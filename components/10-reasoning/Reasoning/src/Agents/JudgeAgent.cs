namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 法官Agent — 最终裁决，基于 DAG 证据链 + 5维客观权重 + 链式传播
/// </summary>
public sealed class JudgeAgent : ReasoningAgentBase
{
    public override AgentRole Role => AgentRole.Judge;
    public override string Name => "法官Agent";

    public override string SystemPrompt =>
        "你是一个公正的法官。你的职责是基于控辩双方的证据链做出裁决。" +
        "对每个假定，根据证据权重和信任度，做出接受(Accept)、驳回(Reject)、部分接受(PartiallyAccept)或待补充(Pending)的裁决。" +
        "输出JSON格式：{\"verdicts\":[{\"claimContent\":\"...\",\"decision\":\"Accept|Reject|PartiallyAccept|Pending\",\"reason\":\"...\",\"confidence\":80}]}";

    private readonly WeightedDecisionSystem _decisionSystem = new();

    public JudgeAgent(ILogger<JudgeAgent> logger, IChatClient? chatClient = null, IAgentMessageBroker? messageBroker = null)
        : base(logger, chatClient, messageBroker) { }

    public override async Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "裁决" };
        var opts = context.Options;

        var pending = context.GetVisibleItemsForRole(Role)
            .Where(x => x.State is DataState.Verified or DataState.Assumption)
            .ToList();

        var visibleEvidence = context.GetVisibleEvidenceForRole(Role);

        foreach (var item in pending)
        {
            var supportingEdgeIds = context.Dag.Edges.Values
                .Where(e => e.ToId == item.Id && e.Label == "SUPPORTS")
                .Select(e => e.FromId)
                .ToHashSet();

            var refutingEdgeIds = context.Dag.Edges.Values
                .Where(e => e.ToId == item.Id && e.Label == "REFUTES")
                .Select(e => e.FromId)
                .ToHashSet();

            var prosEvidence = visibleEvidence
                .Where(e => e.SubmittedBy == AgentRole.Prosecutor && supportingEdgeIds.Contains(e.Id))
                .ToList();

            var defEvidence = visibleEvidence
                .Where(e => e.SubmittedBy == AgentRole.Defender && refutingEdgeIds.Contains(e.Id))
                .ToList();

            var verdict = DecideWithWeightedSystem(item, prosEvidence, defEvidence, opts);
            if (verdict is not null)
            {
                action.Verdicts.Add(verdict);
            }
        }

        if (pending.Count > 0)
        {
            var itemsText = string.Join("\n", pending.Select((x, i) =>
                $"{i + 1}. [{x.State}] {x.Content} (置信度:{x.Confidence}%)"));

            var prosCount = visibleEvidence.Count(e => e.SubmittedBy == AgentRole.Prosecutor);
            var defCount = visibleEvidence.Count(e => e.SubmittedBy == AgentRole.Defender);
            var userPrompt = $"请对以下假定做出裁决：\n{itemsText}\n\n当前证据概况：控方{prosCount}条，辩方{defCount}条";

            userPrompt = await CompressPromptIfNeededAsync(context, Role, userPrompt, ct);

            var (llmResponse, usage, promptTokens) = await CallLlmAsync(userPrompt, temperature: context.Options.JudgeTemperature, maxTokens: context.Options.DefaultLlmMaxTokens, ct: ct).ConfigureAwait(false);
            if (llmResponse is not null)
            {
                foreach (var v in ParseVerdictsFromLlmResponse(llmResponse, pending))
                {
                    var existing = action.Verdicts.FirstOrDefault(x => x.ClaimId == v.ClaimId);
                    if (existing is null)
                    {
                        action.Verdicts.Add(v);
                    }
                }
            }

            if (usage is not null)
            {
                action.TokensUsed = usage.TotalTokens + promptTokens;
            }
            else
            {
                action.TokensUsed = promptTokens;
            }

            await BroadcastAsync("verdict_issued",
                $"法官对 {pending.Count} 个项目做出了 {action.Verdicts.Count} 项裁决", ct).ConfigureAwait(false);
        }

        return action;
    }

    /// <summary>
    /// 使用加权决策系统裁决 — 5维客观权重 + 链式传播
    /// </summary>
    private Verdict? DecideWithWeightedSystem(
        DataItem item,
        IReadOnlyList<EvidenceRecord> prosEvidence,
        IReadOnlyList<EvidenceRecord> defEvidence,
        ReasoningOptions opts)
    {
        if (prosEvidence.Count == 0 && defEvidence.Count == 0) return null;

        var result = _decisionSystem.MakeWeightedDecision(prosEvidence, defEvidence);

        var prosWeight = result.ProsecutionWeight;
        var defWeight = result.DefenseWeight;

        if (prosWeight >= opts.AcceptThreshold && prosWeight > defWeight * opts.AcceptMultiplier)
        {
            return new Verdict
            {
                ClaimId = item.Id,
                Decision = VerdictDecision.Accept,
                Reason = $"控方证据充分 (加权:{prosWeight:F2} vs {defWeight:F2}, 拓扑:{result.TopologyImpact:F2}, 信念一致性:{result.BeliefConsistency:F2})",
                Confidence = Math.Min(100, 70 + (int)(result.FinalConfidence * 30)),
            };
        }

        if (defWeight > prosWeight * opts.RejectMultiplier)
        {
            return new Verdict
            {
                ClaimId = item.Id,
                Decision = VerdictDecision.Reject,
                Reason = $"辩方证据更有力 (加权:{defWeight:F2} vs {prosWeight:F2})",
                Confidence = Math.Min(100, 60 + (int)(result.FinalConfidence * 30)),
            };
        }

        if (prosWeight > 0 && defWeight > 0 && Math.Abs(prosWeight - defWeight) < opts.PendingWeightDelta)
        {
            return new Verdict
            {
                ClaimId = item.Id,
                Decision = VerdictDecision.Pending,
                Reason = $"控辩双方证据相当 (加权:{prosWeight:F2} vs {defWeight:F2})，需要补充证据",
                Confidence = 50,
            };
        }

        if (prosWeight > 0 && defWeight > 0 && prosWeight > defWeight && prosWeight < defWeight * opts.AcceptMultiplier)
        {
            return new Verdict
            {
                ClaimId = item.Id,
                Decision = VerdictDecision.PartiallyAccept,
                Reason = $"控方证据占优但不足以完全确认 (加权:{prosWeight:F2} vs {defWeight:F2})",
                Confidence = Math.Min(100, 50 + (int)(result.FinalConfidence * 20)),
            };
        }

        return null;
    }

    private List<Verdict> ParseVerdictsFromLlmResponse(string content, IReadOnlyList<DataItem> pending)
    {
        var verdicts = new List<Verdict>();
        try
        {
            var json = ExtractJsonObject(content);
            if (json is null) return verdicts;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("verdicts", out var vArray)) return verdicts;

            foreach (var item in vArray.EnumerateArray())
            {
                var claimContent = item.TryGetProperty("claimContent", out var cc) ? cc.GetString() : null;
                var claim = claimContent is not null
                    ? pending.FirstOrDefault(p => p.Content.Contains(claimContent, StringComparison.Ordinal))
                    : null;

                verdicts.Add(new Verdict
                {
                    ClaimId = claim?.Id ?? string.Empty,
                    Decision = item.TryGetProperty("decision", out var d) ? ParseDecision(d.GetString()) : VerdictDecision.Pending,
                    Reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty,
                    Confidence = item.TryGetProperty("confidence", out var c) ? c.GetInt32() : 50,
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[法官] 解析LLM裁决JSON失败");
        }

        return verdicts;
    }

    private static VerdictDecision ParseDecision(string? value) => value switch
    {
        "Accept" => VerdictDecision.Accept,
        "Reject" => VerdictDecision.Reject,
        "PartiallyAccept" => VerdictDecision.PartiallyAccept,
        _ => VerdictDecision.Pending,
    };
}
