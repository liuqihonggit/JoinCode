namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 控方Agent — 主动寻找证据，提出指控
/// </summary>
public sealed class ProsecutorAgent : ReasoningAgentBase
{
    public override AgentRole Role => AgentRole.Prosecutor;
    public override string Name => "控方Agent";

    public override string SystemPrompt =>
        "你是一个严谨的检察官。你的职责是为每个假定寻找支持证据。" +
        "审查所有假定，对每个假定提出至少一条支持证据。" +
        "证据必须包含：内容描述、来源、信任度(DirectEvidence|StrongCorroboration|Moderate|Weak|Hearsay|Unreliable)、权重(0.1-10.0)。" +
        "输出JSON格式：{\"evidence\":[{\"content\":\"...\",\"source\":\"...\",\"trustLevel\":\"Moderate\",\"weight\":1.0}]}";

    public ProsecutorAgent(ILogger<ProsecutorAgent> logger, IChatClient? chatClient = null, IAgentMessageBroker? messageBroker = null)
        : base(logger, chatClient, messageBroker) { }

    public override async Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
    {
        var action = new AgentAction { AgentRole = Role, ActionType = "提出证据" };

        var unverified = context.AllItems
            .Where(x => x.State == DataState.Assumption)
            .ToList();

        foreach (var item in unverified)
        {
            action.AffectedClaimIds.Add(item.Id);
        }

        if (unverified.Count > 0)
        {
            var claimsText = string.Join("\n", unverified.Select((x, i) => $"{i + 1}. {x.Content}"));
            var userPrompt = $"请为以下假定各提出至少一条支持证据：\n{claimsText}";

            var (llmResponse, usage) = await CallLlmAsync(userPrompt, temperature: context.Options.ProsecutorTemperature, maxTokens: context.Options.DefaultLlmMaxTokens, ct: ct).ConfigureAwait(false);
            if (llmResponse is not null)
            {
                foreach (var e in ParseEvidenceFromLlmResponse(llmResponse))
                {
                    action.Evidence.Add(e);
                }
            }

            if (usage is not null)
            {
                action.TokensUsed = usage.TotalTokens;
            }

            await SendMessageAsync(AgentRole.Defender.ToValue(), "evidence_submitted",
                $"控方为 {unverified.Count} 个假定提交了 {action.Evidence.Count} 条证据", ct).ConfigureAwait(false);
        }

        return action;
    }

    private List<EvidenceRecord> ParseEvidenceFromLlmResponse(string content)
    {
        var records = new List<EvidenceRecord>();
        try
        {
            var json = ExtractJsonObject(content);
            if (json is null) return records;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("evidence", out var evidenceArray)) return records;

            foreach (var item in evidenceArray.EnumerateArray())
            {
                records.Add(new EvidenceRecord
                {
                    Content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                    Source = item.TryGetProperty("source", out var s) ? s.GetString() : "LLM生成",
                    TrustLevel = item.TryGetProperty("trustLevel", out var t) ? ParseTrustLevel(t.GetString()) : TrustLevel.Moderate,
                    Weight = item.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0,
                    Category = EvidenceCategory.Documentary,
                    SubmittedBy = AgentRole.Prosecutor,
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[控方] 解析LLM证据JSON失败");
        }

        return records;
    }
}
