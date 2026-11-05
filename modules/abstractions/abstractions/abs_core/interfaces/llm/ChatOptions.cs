namespace JoinCode.Abstractions.LLM;

/// <summary>聊天请求选项。</summary>
public sealed class ChatOptions {
    /// <summary>获取或设置采样温度。</summary>
    public float? Temperature { get; init; }
    /// <summary>获取或设置最大生成 token 数。</summary>
    public int? MaxTokens { get; init; }
    /// <summary>获取或设置核采样概率。</summary>
    public float? TopP { get; init; }
    /// <summary>获取或设置频率惩罚。</summary>
    public float? FrequencyPenalty { get; init; }
    /// <summary>获取或设置存在惩罚。</summary>
    public float? PresencePenalty { get; init; }
    /// <summary>获取或设置工具选择策略。</summary>
    public ToolChoice ToolChoice { get; init; }
    /// <summary>获取或设置已发现的工具集。</summary>
    public DiscoveredToolSet? DiscoveredTools { get; init; }
    /// <summary>获取或设置延迟加载的工具列表。</summary>
    public IReadOnlyList<DeferredToolInfo> DeferredTools { get; init; } = [];
    /// <summary>获取或设置扩展数据字典。</summary>
    public IReadOnlyDictionary<string, JsonElement> ExtensionData { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>
    /// 推理力度
    /// 映射到 Anthropic budget_tokens 或 OpenAI reasoning_effort
    /// </summary>
    public EffortLevel? EffortLevel { get; init; }

    /// <summary>
    /// 思考模式开关 — DeepSeek V4 等模型通过 thinking:{"type":"enabled"} 显式开启思考模式
    /// 由上层基于 AppState.ThinkingEnabled 和供应商能力决定是否开启
    /// </summary>
    public bool ThinkingEnabled { get; init; }

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

    /// <summary>获取默认聊天选项。</summary>
    public static ChatOptions Default => new() {
        Temperature = 0.7f,
        MaxTokens = 4000,
        TopP = 0.95f
    };

    /// <summary>
    /// EffortLevel 到 Anthropic budget_tokens 的映射
    /// </summary>
    public static int EffortToBudgetTokens(EffortLevel effortLevel) => effortLevel switch {
        JoinCode.Abstractions.LLM.EffortLevel.Low => 4000,
        JoinCode.Abstractions.LLM.EffortLevel.Medium => 10000,
        JoinCode.Abstractions.LLM.EffortLevel.High => 32000,
        JoinCode.Abstractions.LLM.EffortLevel.Max => 64000,
        _ => 32000
    };

    /// <summary>
    /// EffortLevel 到 OpenAI reasoning_effort 的映射
    /// </summary>
    public static string EffortToReasoningEffort(EffortLevel effortLevel) => effortLevel switch {
        JoinCode.Abstractions.LLM.EffortLevel.Low => JoinCode.Abstractions.LLM.EffortLevel.Low.ToValue(),
        JoinCode.Abstractions.LLM.EffortLevel.Medium => JoinCode.Abstractions.LLM.EffortLevel.Medium.ToValue(),
        JoinCode.Abstractions.LLM.EffortLevel.High => JoinCode.Abstractions.LLM.EffortLevel.High.ToValue(),
        JoinCode.Abstractions.LLM.EffortLevel.Max => JoinCode.Abstractions.LLM.EffortLevel.High.ToValue(),
        _ => JoinCode.Abstractions.LLM.EffortLevel.High.ToValue()
    };
}
