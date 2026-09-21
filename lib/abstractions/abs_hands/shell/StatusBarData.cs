namespace JoinCode.Abstractions.Shell;

/// <summary>
/// 状态栏数据 — 对齐 TS StatusBar 组件数据模型
/// 与 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/> 的关系：
/// InputTokens ← PromptTokens, OutputTokens ← CompletionTokens
/// </summary>
public sealed class StatusBarData {
    /// <summary>获取或设置模型名称。</summary>
    public string Model { get; set; } = "";
    /// <summary>获取或设置输入令牌数。</summary>
    public int InputTokens { get; set; }
    /// <summary>获取或设置输出令牌数。</summary>
    public int OutputTokens { get; set; }
    /// <summary>获取或设置上下文窗口大小。</summary>
    public int ContextWindowSize { get; set; }
    /// <summary>获取或设置累计费用(美元)。</summary>
    public decimal TotalCostUsd { get; set; }
    /// <summary>获取或设置推理努力级别。</summary>
    public JoinCode.Abstractions.LLM.EffortLevel EffortLevel { get; set; } = JoinCode.Abstractions.LLM.EffortLevel.Auto;
    /// <summary>获取或设置会话名称。</summary>
    public string? SessionName { get; set; }
    /// <summary>获取或设置工作树会话。</summary>
    public string? WorktreeSession { get; set; }
    /// <summary>获取或设置速率限制已用百分比。</summary>
    public double? RateLimitUsedPercentage { get; set; }
    /// <summary>获取或设置速率限制重置时间。</summary>
    public DateTime? RateLimitResetsAt { get; set; }
    /// <summary>获取或设置权限模式。</summary>
    public PermissionMode PermissionMode { get; set; } = PermissionMode.Auto;

    /// <summary>获取总令牌数。</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>获取上下文窗口已用百分比。</summary>
    public double UsedPercentage => ContextWindowSize > 0
        ? (double)TotalTokens / ContextWindowSize * 100
        : 0;
}