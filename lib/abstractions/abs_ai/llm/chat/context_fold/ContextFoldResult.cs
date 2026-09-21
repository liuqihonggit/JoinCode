namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ContextFoldResult {
    /// <summary>获取是否已折叠。</summary>
    public bool Folded { get; init; }
    /// <summary>获取头部消息数。</summary>
    public int HeadMessageCount { get; init; }
    /// <summary>获取尾部消息数。</summary>
    public int TailMessageCount { get; init; }
    /// <summary>获取原始消息数。</summary>
    public int OriginalMessageCount { get; init; }
    /// <summary>获取摘要文本。</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>获取折叠决策。</summary>
    public ContextFoldDecision Decision { get; init; }

    /// <summary>本次折叠前执行的工具结果剪裁统计（无可剪裁内容时为空）。</summary>
    public SnipStats? Snip { get; init; }
}