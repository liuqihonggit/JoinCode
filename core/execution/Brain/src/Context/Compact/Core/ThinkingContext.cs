namespace Core.Context.Compact;

/// <summary>
/// Thinking 模式上下文 — 封装 API 端上下文管理所需的 thinking 状态
/// </summary>
public sealed record ThinkingContext
{
    /// <summary>
    /// 是否启用 thinking 模式
    /// </summary>
    public required bool HasThinking { get; init; }

    /// <summary>
    /// 是否启用 redact-thinking
    /// </summary>
    public bool IsRedactThinkingActive { get; init; }

    /// <summary>
    /// 是否清除全部 thinking（>1h 空闲时）
    /// </summary>
    public bool ClearAllThinking { get; init; }
}
