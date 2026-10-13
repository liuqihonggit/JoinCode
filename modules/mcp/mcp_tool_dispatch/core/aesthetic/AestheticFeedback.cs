namespace McpToolRegistry;

/// <summary>
/// 情绪反馈 — 工具结果分析后生成的情绪化提示，注入到 InjectedMessages 引导 AI 行为
/// </summary>
public sealed record AestheticFeedback {
    /// <summary>情绪类型 — 与代码品味强关联的向量</summary>
    public required AestheticEmotion Emotion { get; init; }

    /// <summary>情绪化提示文本 — 包含情绪标点，直接包裹情绪向量</summary>
    public required string Message { get; init; }

    /// <summary>建议操作 — 引导 AI 接下来该怎么做（正确写法）</summary>
    public string? Suggestion { get; init; }
}
