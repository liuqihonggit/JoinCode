namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 辩方Agent — 质疑证据，寻找反驳
/// </summary>
public sealed class DefenderAgent : ReasoningAgent {
    /// <summary>
    /// 系统提示词 — 指示辩方Agent 质疑控方证据可靠性并寻找反驳证据，输出 JSON 格式
    /// </summary>
    public override string SystemPrompt =>
        "你是一个犀利的辩护律师。你的职责是质疑控方证据的可靠性，寻找反驳证据。" +
        "审查所有已验证项和假定，如果控方证据不足或存在漏洞，提出质疑和反驳证据。" +
        "输出JSON格式：{\"counterEvidence\":[{\"content\":\"...\",\"source\":\"...\",\"trustLevel\":\"Moderate\",\"weight\":1.0}],\"doubts\":[\"质疑1\",\"质疑2\"]}";

    /// <summary>
    /// 构造辩方Agent，注入查询引擎、日志器，可选聊天客户端与消息邮箱
    /// </summary>
    /// <param name="queryEngine">查询引擎，用于检索推理上下文</param>
    /// <param name="logger">日志器，记录 Agent 运行日志</param>
    /// <param name="chatClient">聊天客户端，可选；为 null 时跳过 LLM 调用</param>
    /// <param name="messageBroker">消息邮箱，可选；用于跨 Agent 通信</param>
    public DefenderAgent(IQueryEngine queryEngine, ILogger<DefenderAgent> logger, IChatClient? chatClient = null, IMailbox? messageBroker = null)
        : base(queryEngine, logger, AgentRole.Defender, "辩方Agent", chatClient, messageBroker) { }

    /// <summary>
    /// 异步执行辩方推理：审查已验证项与假定，对证据链不完整者提出质疑，并调用 LLM 生成反驳证据
    /// </summary>
    /// <param name="context">推理上下文，提供可见数据项、证据与 DAG 边</param>
    /// <param name="ct">取消令牌，用于中断异步操作</param>
    /// <returns>辩方 Agent 动作结果，包含质疑、反驳证据与令牌用量</returns>
    public override async Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct) {
        var action = new AgentAction { AgentRole = Role, ActionType = "质疑证据" };
        var doubtThreshold = context.Options.DefenderDoubtThreshold;

        var targets = context.GetVisibleItemsForRole(Role)
            .Where(x => x.State == DataState.Verified || x.State == DataState.Assumption)
            .ToList();

        var visibleEvidence = context.GetVisibleEvidenceForRole(Role);

        foreach (var item in targets) {
            action.AffectedClaimIds.Add(item.Id);

            var supportingEdgeIds = context.Dag.Edges.Values
                .Where(e => e.ToId == item.Id && e.Label == "SUPPORTS")
                .Select(e => e.FromId)
                .ToHashSet();

            var supportingEvidence = visibleEvidence
                .Count(e => e.SubmittedBy == AgentRole.Prosecutor && supportingEdgeIds.Contains(e.Id));

            if (supportingEvidence < doubtThreshold) {
                action.Doubts.Add($"证据链不完整: {item.Content} (控方证据数:{supportingEvidence}, 阈值:{doubtThreshold})");
                action.ActionType = "质疑";
            }
        }

        if (targets.Count > 0) {
            var itemsText = string.Join("\n", targets.Select((x, i) => $"{i + 1}. [{x.State}] {x.Content}"));
            var userPrompt = $"请审查以下项目，提出反驳证据和质疑：\n{itemsText}";

            userPrompt = await CompressPromptIfNeededAsync(context, Role, userPrompt, ct).ConfigureAwait(false);

            var (llmResponse, usage, promptTokens) = await CallLlmAsync(userPrompt, temperature: context.Options.DefenderTemperature, maxTokens: context.Options.DefaultLlmMaxTokens, ct: ct).ConfigureAwait(false);
            if (llmResponse is not null) {
                var (counterEvidence, doubts) = ParseCounterEvidenceFromLlmResponse(llmResponse);
                foreach (var e in counterEvidence) {
                    action.CounterEvidence.Add(e);
                }
                foreach (var d in doubts) {
                    action.Doubts.Add(d);
                }
            }

            if (usage is not null) {
                action.TokensUsed = usage.TotalTokens + promptTokens;
            } else {
                action.TokensUsed = promptTokens;
            }

            await SendMessageAsync(AgentRole.Judge.ToValue(), "counter_evidence_submitted",
                $"辩方对 {targets.Count} 个项目提交了 {action.CounterEvidence.Count} 条反驳和 {action.Doubts.Count} 个质疑", ct).ConfigureAwait(false);
        }

        return action;
    }

    private (List<EvidenceRecord> CounterEvidence, List<string> Doubts) ParseCounterEvidenceFromLlmResponse(string content) {
        var counterEvidence = new List<EvidenceRecord>();
        var doubts = new List<string>();

        try {
            var json = ExtractJsonObject(content, _logger);
            if (json is null) return (counterEvidence, doubts);

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("counterEvidence", out var ceArray)) {
                foreach (var item in ceArray.EnumerateArray()) {
                    counterEvidence.Add(new EvidenceRecord {
                        Content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                        Source = item.TryGetProperty("source", out var s) ? s.GetString() : "LLM生成",
                        TrustLevel = item.TryGetProperty("trustLevel", out var t) ? ParseTrustLevel(t.GetString()) : TrustLevel.Moderate,
                        Weight = item.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0,
                        Category = EvidenceCategory.Documentary,
                        SubmittedBy = AgentRole.Defender,
                    });
                }
            }

            if (doc.RootElement.TryGetProperty("doubts", out var dArray)) {
                foreach (var item in dArray.EnumerateArray()) {
                    var doubt = item.GetString();
                    if (doubt is not null) doubts.Add(doubt);
                }
            }
        } catch (JsonException ex) {
            _logger.LogWarning(ex, "[辩方] 解析LLM反驳JSON失败");
        }

        return (counterEvidence, doubts);
    }
}