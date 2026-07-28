namespace JoinCode.Abstractions.LLM;

public sealed class ChatOptions
{
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public float? TopP { get; init; }
    public float? FrequencyPenalty { get; init; }
    public float? PresencePenalty { get; init; }
    public ToolChoice ToolChoice { get; init; }
    public DiscoveredToolSet? DiscoveredTools { get; init; }
    public IReadOnlyList<DeferredToolInfo>? DeferredTools { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    /// 推理力度
    /// 映射到 Anthropic budget_tokens 或 OpenAI reasoning_effort
    /// </summary>
    public EffortLevel? EffortLevel { get; init; }

    /// <summary>
    /// 快速模式: 启用后使用更小/更快的模型
    /// 通过 ExtensionData["model"] 传递快速模型 ID
    /// </summary>
    public bool FastMode { get; init; }

    /// <summary>
    /// 快速模式下使用的模型 ID（可选）
    /// </summary>
    public string? FastModelId { get; init; }

    /// <summary>
    /// API 端上下文管理配置 — 对齐 TS getAPIContextManagement
    /// 通过 context_management 请求参数让 Anthropic API 在服务端自动清理工具结果
    /// 不破坏 prompt cache，优于客户端 microcompact
    /// </summary>
    public ContextManagementConfig? ContextManagement { get; init; }

    public static ChatOptions Default => new()
    {
        Temperature = 0.7f,
        MaxTokens = 4000,
        TopP = 0.95f
    };

    /// <summary>
    /// EffortLevel 到 Anthropic budget_tokens 的映射
    /// </summary>
    public static int EffortToBudgetTokens(EffortLevel effortLevel) => effortLevel switch
    {
        JoinCode.Abstractions.LLM.EffortLevel.Low => 4000,
        JoinCode.Abstractions.LLM.EffortLevel.Medium => 10000,
        JoinCode.Abstractions.LLM.EffortLevel.High => 32000,
        JoinCode.Abstractions.LLM.EffortLevel.Max => 64000,
        _ => 32000
    };

    /// <summary>
    /// EffortLevel 到 OpenAI reasoning_effort 的映射
    /// </summary>
    public static string EffortToReasoningEffort(EffortLevel effortLevel) => effortLevel switch
    {
        JoinCode.Abstractions.LLM.EffortLevel.Low => "low",
        JoinCode.Abstractions.LLM.EffortLevel.Medium => "medium",
        JoinCode.Abstractions.LLM.EffortLevel.High => "high",
        JoinCode.Abstractions.LLM.EffortLevel.Max => "high",
        _ => "high"
    };
}
