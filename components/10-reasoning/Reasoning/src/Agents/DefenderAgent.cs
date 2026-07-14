namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 辩方Agent — 质疑证据，寻找反驳
/// </summary>
public sealed class DefenderAgent : ReasoningAgentBase
{
    public override AgentRole Role => AgentRole.Defender;
    public override string Name => "辩方Agent";

    public override string SystemPrompt =>
        "你是一个犀利的辩护律师。你的职责是质疑控方证据的可靠性，寻找反驳证据。" +
        "审查所有已验证项和假定，如果控方证据不足或存在漏洞，提出质疑和反驳证据。" +
        "输出JSON格式：{\"counterEvidence\":[{\"content\":\"...\",\"source\":\"...\",\"trustLevel\":\"Moderate\",\"weight\":1.0}],\"doubts\":[\"质疑1\",\"质疑2\"]}";

    public DefenderAgent(ILogger<DefenderAgent> logger, IChatClient? chatClient = null, IAgentMessageBroker? messageBroker = null)
        : base(logger, chatClient, messageBroker) { }

    public override async Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "质疑证据" };
        var doubtThreshold = context.Options.DefenderDoubtThreshold;

        var targets = context.GetVisibleItemsForRole(Role)
            .Where(x => x.State == DataState.Verified || x.State == DataState.Assumption)
            .ToList();

        var visibleEvidence = context.GetVisibleEvidenceForRole(Role);

        foreach (var item in targets)
        {
            action.AffectedClaimIds.Add(item.Id);

            var supportingEdgeIds = context.Dag.Edges.Values
                .Where(e => e.ToId == item.Id && e.Label == "SUPPORTS")
                .Select(e => e.FromId)
                .ToHashSet();

            var supportingEvidence = visibleEvidence
                .Count(e => e.SubmittedBy == AgentRole.Prosecutor && supportingEdgeIds.Contains(e.Id));

            if (supportingEvidence < doubtThreshold)
            {
                action.Doubts.Add($"证据链不完整: {item.Content} (控方证据数:{supportingEvidence}, 阈值:{doubtThreshold})");
                action.ActionType = "质疑";
            }
        }

        if (targets.Count > 0)
        {
            var itemsText = string.Join("\n", targets.Select((x, i) => $"{i + 1}. [{x.State}] {x.Content}"));
            var userPrompt = $"请审查以下项目，提出反驳证据和质疑：\n{itemsText}";

            userPrompt = await CompressPromptIfNeededAsync(context, Role, userPrompt, ct);

            var (llmResponse, usage, promptTokens) = await CallLlmAsync(userPrompt, temperature: context.Options.DefenderTemperature, maxTokens: context.Options.DefaultLlmMaxTokens, ct: ct).ConfigureAwait(false);
            if (llmResponse is not null)
            {
                var (counterEvidence, doubts) = ParseCounterEvidenceFromLlmResponse(llmResponse);
                foreach (var e in counterEvidence)
                {
                    action.CounterEvidence.Add(e);
                }
                foreach (var d in doubts)
                {
                    action.Doubts.Add(d);
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

            await SendMessageAsync(AgentRole.Judge.ToValue(), "counter_evidence_submitted",
                $"辩方对 {targets.Count} 个项目提交了 {action.CounterEvidence.Count} 条反驳和 {action.Doubts.Count} 个质疑", ct).ConfigureAwait(false);
        }

        return action;
    }

    private (List<EvidenceRecord> CounterEvidence, List<string> Doubts) ParseCounterEvidenceFromLlmResponse(string content)
    {
        var counterEvidence = new List<EvidenceRecord>();
        var doubts = new List<string>();

        try
        {
            var json = ExtractJsonObject(content);
            if (json is null) return (counterEvidence, doubts);

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("counterEvidence", out var ceArray))
            {
                foreach (var item in ceArray.EnumerateArray())
                {
                    counterEvidence.Add(new EvidenceRecord
                    {
                        Content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                        Source = item.TryGetProperty("source", out var s) ? s.GetString() : "LLM生成",
                        TrustLevel = item.TryGetProperty("trustLevel", out var t) ? ParseTrustLevel(t.GetString()) : TrustLevel.Moderate,
                        Weight = item.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0,
                        Category = EvidenceCategory.Documentary,
                        SubmittedBy = AgentRole.Defender,
                    });
                }
            }

            if (doc.RootElement.TryGetProperty("doubts", out var dArray))
            {
                foreach (var item in dArray.EnumerateArray())
                {
                    var doubt = item.GetString();
                    if (doubt is not null) doubts.Add(doubt);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[辩方] 解析LLM反驳JSON失败");
        }

        return (counterEvidence, doubts);
    }
}
